using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using Apitron.Bluetooth;
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
}
