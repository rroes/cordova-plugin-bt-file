/* Copyright by Apitron LTD 2014,
 The code is free to use in any project.
 Author Robert McKalkin.*/

using System;
using System.IO;

namespace Apitron.Bluetooth
{
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
