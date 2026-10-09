// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Gradiwin.Services;

/// <summary>Opens the Windows Share sheet for a file from a desktop window (IDataTransferManagerInterop).</summary>
public static class ShareHelper
{
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        nint GetForWindow([In] nint appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(nint appWindow);
    }

    private static readonly Guid s_dataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    public static async Task ShareFileAsync(nint hwnd, string path, string title)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = s_dataTransferManagerIid;
        var ptr = interop.GetForWindow(hwnd, ref iid);
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);

        void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            args.Request.Data.Properties.Title = title;
            args.Request.Data.SetStorageItems([file]);
            args.Request.Data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            sender.DataRequested -= OnRequested;
        }

        manager.DataRequested += OnRequested;
        interop.ShowShareUIForWindow(hwnd);
    }
}
