/* Copyright by Apitron LTD 2014,
 The code is free to use in any project.
 Author Robert McKalkin.*/

namespace Apitron.Bluetooth
{
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
