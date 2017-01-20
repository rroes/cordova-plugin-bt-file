/* Copyright by Apitron LTD 2014,
 The code is free to use in any project.
 Author Robert McKalkin.*/

namespace Apitron.Bluetooth
{
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
}
