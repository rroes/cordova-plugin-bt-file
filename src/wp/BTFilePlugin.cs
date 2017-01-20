using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using Windows.Networking.Proximity;
using Windows.Storage;
using WPCordovaClassLib.Cordova.JSON;
using System.IO;
using Windows.Storage.Streams;
using System.Threading.Tasks;

namespace WPCordovaClassLib.Cordova.Commands
{
    public class BTFilePlugin : BaseCommand
    {

        private ObservableCollection<PeerInformation> bluetoothDevices;


        public BTFilePlugin()
		{
			bluetoothDevices = new ObservableCollection<PeerInformation>();
		}
		

        public async void PairedDeviceList(string jsonArgs)
        {
            try
            {

                // look for paired devices and update our listbox
                PeerFinder.AlternateIdentities["Bluetooth:Paired"] = "";
                IReadOnlyList<PeerInformation> result = await PeerFinder.FindAllPeersAsync();

                List<string> list = new List<string>();
                
                for (int i = 0; i < result.Count; ++i)
                {
                    bluetoothDevices.Insert(i, result[i]);
                    list.Add(bluetoothDevices[i].DisplayName);
                }
                DispatchCommandResult(new PluginResult(PluginResult.Status.OK, list));
            }
            catch (Exception ex)
            {
                // suggested by MS sample, handles BT radio off case
                if ((uint)ex.HResult == 0x8007048F)
                {
                    MessageBox.Show("The Bluetooth radio appears to be off.", "Error", MessageBoxButton.OK);
                }
            }
        }

        public async void SendFileViaBT(string jsonArgs)
        {
            var options = JsonHelper.Deserialize<string[]>(jsonArgs);

            // Parameters: fileData, filename, device
            string filedata = options[0];
            string filename = options[1];
            int device = Int32.Parse(options[2]);


            //FileData lokal ablegen
            filename = await saveFileData(filedata, filename);

            PeerInformation item = bluetoothDevices[device] as PeerInformation;

            using (BluetoothManager manager = new BluetoothManager(item))
            {
                // Create sample file; replace if exists
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                StorageFile file = await folder.GetFileAsync(filename);


                if (!await manager.SendFile(file, filename, BluetoothProfile.OBEXOPP, null))
                {
                    DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, item.DisplayName));
                }
                else
                {
                    // do some bbzzz when ok
                    Microsoft.Devices.VibrateController.Default.Start(new TimeSpan(0, 0, 0, 0, 500));
                    DispatchCommandResult(new PluginResult(PluginResult.Status.OK, item.DisplayName));
                }
            }

        }



        private async Task<string> saveFileData(string fileData, string filename)
        {
            try
            {

                byte[] fileBytes = Convert.FromBase64String(fileData);


                using (var fileStream = new MemoryStream(fileBytes))
                {
                    fileStream.Seek(0, SeekOrigin.Begin);

                    // Create sample file; replace if exists
                    StorageFolder folder = ApplicationData.Current.LocalFolder;
                    StorageFile file = await folder.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting);

                    using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
                        {
                            using (DataWriter dataWriter = new DataWriter(outputStream))
                            {
                                dataWriter.WriteBytes(fileBytes);
                                await dataWriter.StoreAsync();
                                dataWriter.DetachStream();
                                return filename;
                            }
                            //await outputStream.FlushAsync();
                        }
                        //await fileStream.FlushAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, ex.Message));
                return null;
            }
        }
    }
	
	/// <summary>
    /// Handles bluetooth related tasks, like copying files etc.
    /// </summary>
    internal sealed class BluetoothManager : IDisposable
    {
        #region Fields

        private readonly PeerInformation peer;
        private DataWriter dataWriter;
        private DataReader dataReader;
        private int maxServerPacket;
        private StreamSocket streamSocket;
        private int connectionId;
        private static readonly string ftpUUID = "{00001106-0000-1000-8000-00805f9b34fb}";
        private readonly string oppUUID = "{00001105-0000-1000-8000-00805f9b34fb}";
        private Action<BluetoothFileTransferProgress> progress;

        #endregion

        #region ctor

        /// <summary>
        /// Initializes a new instance of the <see cref="BluetoothManager"/> class.
        /// </summary>
        /// <param name="peer">The peer information object, which is used for connection.</param>
        public BluetoothManager(PeerInformation peer)
        {
            this.peer = peer;

            connectionId = -1;
        }

        #endregion

        #region Members

        /// <summary>
        /// Sends the file using selected <see cref="BluetoothProfile"/> and uses optional callback for monitoring progress.
        /// </summary>
        /// <param name="fs">The fs.</param>
        /// <param name="objectName">Name of the object.</param>
        /// <param name="profile">The profile, please note that only OBEXOPP has been tested at the moment, support for OBEXFTP is present but not tested.</param>
        /// <param name="progress">The progress callback.</param>
        /// <returns>True if operation succeeds, otherwise false.</returns>
        public async Task<bool> SendFile(StorageFile fs, string objectName, BluetoothProfile profile,
                                         Action<BluetoothFileTransferProgress> progress)
        {
            if (fs == null)
            {
                return false;
            }

            try
            {
                using (Stream stream = await fs.OpenStreamForReadAsync())
                {
                    return await SendFile(stream, objectName, profile, progress);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }

            return false;
        }

        /// <summary>
        /// Sends the file using selected <see cref="BluetoothProfile"/> and uses optional callback for monitoring progress.
        /// </summary>
        /// <param name="stream">The stream to read data from, starting position should be zero.</param>
        /// <param name="objectName">Name of the object.</param>
        /// <param name="profile">The profile, please note that only OBEXOPP has been tested at the moment, support for OBEXFTP is present but not tested.</param>
        /// <param name="progress">The progress callback.</param>
        /// <returns>True if operation succeeds, otherwise false.</returns>
        /// <exception cref="ArgumentException">
        /// If stream is empty or objectName is null or empty.
        /// </exception>
        public async Task<bool> SendFile(Stream stream, string objectName, BluetoothProfile profile,
                                         Action<BluetoothFileTransferProgress> progress)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (string.IsNullOrEmpty(objectName))
            {
                throw new ArgumentNullException("objectName");
            }

            this.progress = progress;

            try
            {
                SetProgress(0, BluetoothFileTransferState.Connecting);

                if (await Connect(profile))
                {
                    return await PushData(profile, objectName, stream);
                }
            }
            catch (Exception e)
            {
                SetProgress(0, BluetoothFileTransferState.Aborted);

                Disconnect();

                Debug.WriteLine(e.Message);
            }

            return false;
        }


        /// <summary>
        /// This method opens a connection using the UUID that corresponds to a given profile, sends data and closes the connection afterwards.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> Connect(BluetoothProfile profile)
        {
            Disconnect();

            streamSocket = new StreamSocket();

            await streamSocket.ConnectAsync(peer.HostName, GetProfileUUID(profile));

            dataReader = new DataReader(streamSocket.InputStream);
            dataWriter = new DataWriter(streamSocket.OutputStream);

            //send client request
            byte[] theConnectPacket = ProfilePacketFactory.CreateConnectPacket(profile);

            dataWriter.WriteBytes(theConnectPacket);
            await dataWriter.StoreAsync();

            // Get response code
            await dataReader.LoadAsync(1);
            byte[] buffer = new byte[1];
            dataReader.ReadBytes(buffer);

            if (buffer[0] == 0xA0) // Success
            {
                // Get length
                await dataReader.LoadAsync(2);
                buffer = new byte[2];
                dataReader.ReadBytes(buffer);

                int length = buffer[0] << 8;
                length += buffer[1];

                // Get rest of packet
                await dataReader.LoadAsync((uint) length - 3);
                buffer = new byte[length - 3];
                dataReader.ReadBytes(buffer);

                int obexVersion = buffer[0];
                int flags = buffer[1];
                maxServerPacket = buffer[2] << 8 + buffer[3];

                // read FTP specific response
                if (profile == BluetoothProfile.OBEXFTP)
                {
                    int connectionIdHeader = buffer[4];
                    connectionId = (buffer[5] << 24) | (buffer[6] << 16) | (buffer[7] << 8) | buffer[8];

                    int whoHeader = buffer[9];
                    int whoHeaderLength = (buffer[10] << 8) | buffer[11];

                    byte[] whoHeaderValue = new byte[whoHeaderLength];

                    Array.Copy(buffer, 12, whoHeaderValue, 0, whoHeaderLength - 3);
                }

                return maxServerPacket > 0;
            }

            return false;
        }

        private async Task<bool> PushData(BluetoothProfile profile, string objectName, Stream stream)
        {
            int blockSize = ProfilePacketFactory.DataPacketSize;

            // Chop data into packets           
            int blocks = (int) Math.Ceiling((float) stream.Length/blockSize);

            byte[] packet;
            bool result = false;

            // it seems that android devices very often respond with 0xcb, after the second packet is sent.
            // According to specification it means "Length header required", but we apparently do send it in our first packet.
            // The workaround found is to ignore this response and continue the transfer, somehow it works.
            bool androidMode = false;

            int i = 0;

            for (i = 0; i < blocks; ++i)
            {
                packet = ProfilePacketFactory.CreateDataPacket(profile, objectName, stream, connectionId, blockSize);

                dataWriter.WriteBytes(packet);
                await dataWriter.StoreAsync();

                // Get response code
                await dataReader.LoadAsync(3);
                byte[] buffer = new byte[3];
                dataReader.ReadBytes(buffer);

                int responseLength = (buffer[1] << 8 & 0xFF00) | (buffer[2] & 0xFF);
                if (responseLength > 3 && responseLength == dataReader.UnconsumedBufferLength)
                {
                    byte[] response = new byte[responseLength - 3];
                    dataReader.ReadBytes(response);
                }

                if (!androidMode)
                {
                    // check the response to be valid
                    if (buffer[0] != 0xA0 && buffer[0] != 0x90 && buffer[0] != 0xcb)
                    {
                        result = false;
                        break;
                    }
                    else if (buffer[0] == 0xA0) // Success
                    {
                        result = true;
                        break;
                    }
                    else if (buffer[0] == 0xcb)
                    {
                        androidMode = true;
                    }
                }

                SetProgress(((float) i/blocks)*100, BluetoothFileTransferState.InProgress);
            }

            //so we sent all blocks and there was an andoid mode on
            if (i == blocks && androidMode)
            {
                result = true;
            }

            // attemp to issue a disconnect packet
            byte[] bytes = new byte[3];
            bytes[0] = 0x81;
            bytes[1] = 0;
            bytes[2] = 3;

            if (dataWriter != null)
            {
                dataWriter.WriteBytes(bytes);
                await dataWriter.StoreAsync();
            }

            if (result)
            {
                SetProgress(100, BluetoothFileTransferState.Completed);
            }

            return result;
        }


        /// <summary>
        /// Gets the profile UUID for corresponding <see cref="BluetoothProfile"/> object.
        /// </summary>
        /// <param name="profile">The profile.</param>
        /// <returns>String representation of the UUID</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">profile</exception>
        private string GetProfileUUID(BluetoothProfile profile)
        {
            switch (profile)
            {
                case BluetoothProfile.OBEXOPP:
                    return oppUUID;
                case BluetoothProfile.OBEXFTP:
                    return ftpUUID;
                default:
                    throw new ArgumentOutOfRangeException("profile");
            }
        }

        /// <summary>
        /// Sets the progress of the operation if the callback has been set.
        /// </summary>
        /// <param name="percentage">The percentage.</param>
        /// <param name="state">The state.</param>
        private async void SetProgress(float percentage, BluetoothFileTransferState state)
        {
            if (progress != null)
            {
                progress(new BluetoothFileTransferProgress(percentage, state));
            }
        }

        /// <summary>
        /// Performs disconnect activities.
        /// </summary>
        private void Disconnect()
        {
            try
            {
                if (streamSocket != null)
                {
                    streamSocket.Dispose();
                    streamSocket = null;
                }

                if (dataReader != null)
                {
                    dataReader.Dispose();
                    dataReader = null;
                }

                if (dataWriter != null)
                {
                    dataWriter.Dispose();
                    dataWriter = null;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        public void AbortOperation()
        {
            Disconnect();
        }

        public void Dispose()
        {
            Disconnect();

            progress = null;
        }

        #endregion
    }
    /// <summary>
    /// An object that <see cref="BluetoothManager"/> uses to describe transfer operation progress.
    /// </summary>
    class BluetoothFileTransferProgress
    {
        #region Fields

        private float percentage;
        private BluetoothFileTransferState state;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the completeness of current operation in percents.
        /// </summary>
        /// <value>
        /// The percent.
        /// </value>
        public float Percentage
        {
            get { return percentage; }
        }

        /// <summary>
        /// Gets the state of the operation.
        /// </summary>
        /// <value>
        /// The state.
        /// </value>
        public BluetoothFileTransferState State
        {
            get { return state; }
        }

        #endregion

        #region ctor

        /// <summary>
        /// Initializes a new instance of the <see cref="BluetoothFileTransferProgress"/> class.
        /// </summary>
        public BluetoothFileTransferProgress(float percentage, BluetoothFileTransferState state)
        {
            this.percentage = percentage;
            this.state = state;
        }

        #endregion
    }
}
    /// <summary>
    /// Defines possible transfer states.
    /// </summary>
    enum BluetoothFileTransferState
    {
        Connecting,
        InProgress,
        Completed,
        Aborted
    }
}	
    /// <summary>
    /// Defines bluetooth profiles that  <see cref="BluetoothManager"/> is able to use.
    /// </summary>
    enum BluetoothProfile
    {
        /// <summary>
        /// The OBEX Object Push Profile.
        /// </summary>
        OBEXOPP,

        /// <summary>
        /// The OBEX File Transfer Profile, please not that support ffor this
        /// </summary>
        OBEXFTP
    }
	
	    /// <summary>
    /// Implements a factory class responsible for creation of the different kinds of packets for <see cref="BluetoothProfile"/>.
    /// </summary>
    static class ProfilePacketFactory
    {
        #region Fields

        /// <summary>
        /// Used for TARGET header in case of OBEXFTP
        /// </summary>
        static readonly byte[] ftpServiceUUID = Guid.Parse("F9EC7BC4-953C-11d2-984E-525400DC9E09").ToByteArray();

        static readonly int maxPacketSize = 2048;

        static readonly int dataPacketSize = 1024;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the size of the data packet, the resulting packet size may differ depending of the profile and data remaining.
        /// In case of OPP or FTP it indicates the size of the BODY part.
        /// </summary>
        /// <default>2048 bytes.</default>        
        public static int DataPacketSize 
        { 
            get
            {
                return dataPacketSize;
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Creates the connect packet, which is used to initiate a connection.
        /// </summary>
        /// <returns></returns>
        public static byte [] CreateConnectPacket(BluetoothProfile profile)
        {
            switch(profile)
            {
                case BluetoothProfile.OBEXOPP:
                    return CreateConnectPacketOPP(maxPacketSize);
                case BluetoothProfile.OBEXFTP:
                    return CreateConnectPacketFTP(maxPacketSize);
                default:
                    throw new ArgumentOutOfRangeException("profile");
            }
        }

        /// <summary>
        /// Creates the data packet based on profile given.
        /// </summary>
        /// <param name="profile">The profile.</param>
        /// <param name="objectName">Name of the object.</param>
        /// <param name="stream">The stream.</param>
        /// <param name="connectionId">The connection identifier.</param>
        /// <param name="blockSize">Size of the block.</param>
        /// <returns></returns>
        public static byte[] CreateDataPacket(BluetoothProfile profile,string objectName, Stream stream, int connectionId, int blockSize )
        {
            switch (profile)
            {
                case BluetoothProfile.OBEXOPP:
                case BluetoothProfile.OBEXFTP:
                    break;
                default:
                    throw new ArgumentOutOfRangeException("profile");
            }

            int remainingBytes = (int)(stream.Length - stream.Position);
            bool lastPacket = false;
            bool firstpacket = stream.Position == 0;
            int totalSize = (int)stream.Length;
            int headerLength = 6;
            byte[] encodedName = null;
            bool issueConnectionIdHeader = connectionId != -1 && profile == BluetoothProfile.OBEXFTP;
 
            if(firstpacket)
            {
                System.Text.UnicodeEncoding encoding = new System.Text.UnicodeEncoding(true, false);

                encodedName = encoding.GetBytes(objectName + new char());

                headerLength = 14 + encodedName.Length;
            }

            if (issueConnectionIdHeader)
            {
                headerLength += 5;
            }

            // find the new bodyLength, in case of last packet         
            if (remainingBytes < blockSize)
            {
                lastPacket = true;
                blockSize = (int)remainingBytes;
            }

            // Create packet
            byte[] packet = new byte[headerLength + blockSize];

            // Build packet
            int offset = 0;
            packet[offset++] = !lastPacket ? (byte)0x02 : (byte)0x82; // 0x02 for the first block, 0x82 for the last block last
            packet[offset++] = (byte)((packet.Length & 0xFF00) >> 8);
            packet[offset++] = (byte)(packet.Length & 0xFF);

            //we need it in case of FTP
            if (issueConnectionIdHeader)
            {
                packet[offset++] = 0xCB; // connection ID header
                packet[offset++] = (byte)(connectionId >> 24);
                packet[offset++] = (byte)((connectionId) >> 16);
                packet[offset++] = (byte)((connectionId) >> 8);
                packet[offset++] = (byte)(connectionId & 0xFF);
            }

            if (firstpacket)
            {
                packet[offset++] = 0xC3; // Length header
                packet[offset++] = (byte)((totalSize & 0xFF000000) >> 24);
                packet[offset++] = (byte)((totalSize & 0xFF0000) >> 16);
                packet[offset++] = (byte)((totalSize & 0xFF00) >> 8);
                packet[offset++] = (byte)(totalSize & 0xFF);

                packet[offset++] = 0x01; // Name header
                packet[offset++] = (byte)(((encodedName.Length + 3) & 0xFF00) >> 8);
                packet[offset++] = (byte)((encodedName.Length + 3) & 0xFF);

                System.Buffer.BlockCopy(encodedName, 0, packet, offset, encodedName.Length);
                offset += encodedName.Length;
            }

            packet[offset++] = (byte)(!lastPacket ? 0x48 : 0x49); // Object body chunk header, 0x49 for the last packet
            packet[offset++] = (byte)(((blockSize + 3) & 0xFF00) >> 8);
            packet[offset++] = (byte)((blockSize + 3) & 0xFF);

            // fill the body
            stream.Read(packet, headerLength, blockSize);

            return packet;
        }

        /// <summary>
        /// Creates the connect packet for FTP.
        /// </summary>
        /// <param name="maxClientPacketSize">Maximum size of the client packet.</param>
        /// <returns></returns>
        private static byte[] CreateConnectPacketFTP(int maxClientPacketSize)
        {
            int packetSize = 10 + ftpServiceUUID.Length;
            byte[] theConnectPacket = new byte[packetSize];

            int offset = 0;
            theConnectPacket[offset++] = 0x80;                       // Connect
            theConnectPacket[offset++] = (byte)((packetSize & 0xFF00) >> 8);       // Packetlength Hi Byte
            theConnectPacket[offset++] = (byte)(packetSize & 0xFF);                    // Packetlength Lo Byte
            theConnectPacket[offset++] = 0x10;                       // Obex v1
            theConnectPacket[offset++] = 0x00;                       // No flags
            theConnectPacket[offset++] = (byte)((maxClientPacketSize & 0xFF00) >> 8);    // 2048 byte client max packet size Hi Byte
            theConnectPacket[offset++] = (byte)(maxClientPacketSize & 0xFF);                    // 2048 byte max packet size Lo Byte    

            theConnectPacket[offset++] = 0x46; // TARGET header
            theConnectPacket[offset++] = (byte)(((ftpServiceUUID.Length + 3) & 0xFF) >> 8);
            theConnectPacket[offset++] = (byte)((ftpServiceUUID.Length + 3) & 0xFF);

            Array.Copy(ftpServiceUUID, 0, theConnectPacket, offset, ftpServiceUUID.Length);

            return theConnectPacket;
        }

        /// <summary>
        /// Creates the connect packet for OPP.
        /// </summary>
        /// <param name="maxClientPacketSize">Maximum size of the client packet.</param>
        /// <returns></returns>
        private static byte[] CreateConnectPacketOPP(int maxClientPacketSize)
        {
            int packetSize = 7;
            byte[] theConnectPacket = new byte[packetSize];

            int offset = 0;
            theConnectPacket[offset++] = 0x80;                       // Connect
            theConnectPacket[offset++] = (byte)((packetSize & 0xFF00) >> 8);       // Packetlength Hi Byte
            theConnectPacket[offset++] = (byte)(packetSize & 0xFF);                    // Packetlength Lo Byte
            theConnectPacket[offset++] = 0x10;                       // Obex v1
            theConnectPacket[offset++] = 0x00;                       // No flags
            theConnectPacket[offset++] = (byte) ((maxClientPacketSize & 0xFF00) >> 8);    // 2048 byte client max packet size Hi Byte
            theConnectPacket[offset++] = (byte) (maxClientPacketSize & 0xFF);                    // 2048 byte max packet size Lo Byte    

            return theConnectPacket;
        }

        #endregion
    }
	
}
