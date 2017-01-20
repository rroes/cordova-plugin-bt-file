var BTFilePlugin = {
    PairedDeviceList: function (successCallback, errorCallback, strInput) {
        cordova.exec(successCallback, errorCallback, "BTFilePlugin", "PairedDeviceList");
    },
    SendFileViaBT: function (successCallback, errorCallback, filedata, filename, device) {
        cordova.exec(successCallback, errorCallback, "BTFilePlugin", "SendFileViaBT", [filedata, filename, device]);
    },
}

module.exports = BTFilePlugin;



