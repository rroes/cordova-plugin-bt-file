/* Copyright by Apitron LTD 2014,
 The code is free to use in any project.
 Author Robert McKalkin.*/

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Networking.Proximity;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Apitron.Bluetooth
{
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
}
