// Saves bytes produced on the server (report exports) as a file download.
window.yasDownloadFile = function (fileName, contentType, bytes) {
    var blob = new Blob([bytes], { type: contentType });
    var url = URL.createObjectURL(blob);
    var link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
};
