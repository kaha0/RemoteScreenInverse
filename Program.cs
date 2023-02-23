using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace RemoteScreenInverse
{
    static class Program
    {
        [DllImport("USER32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern void mouse_event(UInt32 dwFlags, UInt32 dx, UInt32 dy, UInt32 dwData, UIntPtr dwExtraInfo);
        [DllImport("USER32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern void keybd_event(byte bVk, byte bScan, UInt32 dwFlags, UIntPtr dwExtraInfo);

        // https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt
        [DllImport("GDI32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern int BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, UInt32 rop);
        // https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-transparentblt
        [DllImport("MSIMG32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern int TransparentBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, UInt32 rop);
        [DllImport("GDI32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
        [DllImport("GDI32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("GDI32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern void DeleteDC(IntPtr hdc);
        [DllImport("GDI32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern void DeleteObject(IntPtr handle);
        [DllImport("USER32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("USER32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern void ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("GDI32.DLL", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hBitmap);

        public static NotifyIcon icon;
        public static ContextMenuStrip menu;
        public static Thread listener;
        public static Rectangle bounds;
        public static Socket socket;
        public static byte quality;
        public static ImageCodecInfo jpgcodec;
        public static ImageCodecInfo tifcodec;
        public static EncoderParameters encparam;
        public static EncoderParameters tifparam;

        public static IntPtr hdcScreen, hdc1, hdc2, hbm1, hbm2, hbmCursor;
        public static bool hbmflipflop = false;
        public static bool cansenddifferential = false;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            icon = new NotifyIcon();
            icon.Icon = Icon.FromHandle(RemoteScreenInverse.Properties.Resources.rsi.GetHicon());
            icon.MouseClick += new MouseEventHandler(icon_MouseClick);
            icon.Visible = true;
            bounds = Screen.GetBounds(Point.Empty);

            hdcScreen = GetDC(IntPtr.Zero);
            hdc1 = CreateCompatibleDC(hdcScreen);
            hdc2 = CreateCompatibleDC(hdcScreen);
            hbm1 = CreateCompatibleBitmap(hdcScreen, bounds.Width, bounds.Height);
            hbm2 = CreateCompatibleBitmap(hdcScreen, bounds.Width, bounds.Height);
            SelectObject(hdc1, hbm1);
            SelectObject(hdc2, hbm2);
            hbmCursor = RemoteScreenInverse.Properties.Resources.cursor.GetHbitmap();

            menu = new ContextMenuStrip();
            ToolStripMenuItem q = new ToolStripMenuItem("Quit");
            q.Click += new EventHandler(Exit);
            menu.Items.Add(q);

            quality = 10;
            foreach (var ici in ImageCodecInfo.GetImageEncoders())
            {
                if (ici.FormatDescription == "JPEG") jpgcodec = ici;
                if (ici.FormatDescription == "TIFF") tifcodec = ici;
            }
            encparam = new EncoderParameters(1);
            tifparam = new EncoderParameters(1);
            tifparam.Param[0] = new EncoderParameter(Encoder.ColorDepth, 1L);

            listener = new Thread(Listen);
            listener.Start();

            Application.Run();
        }

        static void icon_MouseClick(object sender, MouseEventArgs e)
        {
            if (menu.Visible) menu.Close();
            else menu.Show(Cursor.Position);
        }

        public static void Exit(object sender, EventArgs e)
        {
            if (Thread.CurrentThread != listener && listener != null) listener.Abort();
            icon.Visible = false;
            icon.Dispose();

            DeleteDC(hdc1);
            DeleteDC(hdc2);
            DeleteObject(hbm1);
            DeleteObject(hbm2);
            DeleteObject(hbmCursor);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            Environment.Exit(0);
        }

        static void Listen()
        {
            while (true)
            {
                if (socket == null || !socket.Connected)
                {
                    cansenddifferential = false;
                    try
                    {
                        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        socket.Connect(new IPAddress(0x100007f), 8765);
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(e.Message, "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Exit(null, null);
                    }
                }

                try
                {
                    byte[] b = new byte[1];
                    socket.Receive(b, 1, SocketFlags.None);
                    switch (b[0])
                    {
                        case 0:
                            {
                                SendScreen();
                                break;
                            }
                        case 1:
                            {
                                // MOUSEEVENTF_LEFTDOWN
                                mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 2:
                            {
                                // MOUSEEVENTF_LEFTUP
                                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 3:
                            {
                                // MOUSEEVENTF_RIGHTDOWN
                                mouse_event(8, 0, 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 4:
                            {
                                // MOUSEEVENTF_RIGHTUP
                                mouse_event(0x10, 0, 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 5:
                            {
                                // MOUSEEVENTF_MIDDLEDOWN
                                mouse_event(0x20, 0, 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 6:
                            {
                                // MOUSEEVENTF_MIDDLEUP
                                mouse_event(0x40, 0, 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 7:
                            {
                                // Set cursor position
                                byte[] bb = new byte[4];
                                socket.Receive(bb);
                                short x = bb[0];
                                x = (short)(x << 8);
                                x += bb[1];

                                short y = bb[2];
                                y = (short)(y << 8);
                                y += bb[3];

                                Cursor.Position = new Point(x, y);
                                break;
                            }
                        case 8:
                            {
                                // OnScreenKeyboard
                                System.Diagnostics.Process p = new System.Diagnostics.Process();
                                p.StartInfo.FileName = "osk";
                                p.Start();
                                break;
                            }
                        case 9:
                            {
                                // decrease quality
                                if (quality > 0)
                                {
                                    --quality;
                                    if (quality == 0) break;
                                    else encparam.Param[0] = new EncoderParameter(Encoder.Quality, (long)(quality * 10));
                                }
                                break;
                            }
                        case 10:
                            {
                                // increase quality
                                if (quality < 10)
                                {
                                    ++quality;
                                    if (quality == 10) break;
                                    else encparam.Param[0] = new EncoderParameter(Encoder.Quality, (long)(quality * 10));
                                }
                                break;
                            }
                        case 11:
                            {
                                socket.Receive(b, 1, SocketFlags.None);
                                // KEYEVENTF_KEYDOWN == 0
                                keybd_event(b[0], 0, 0, UIntPtr.Zero);
                                break;
                            }
                        case 12:
                            {
                                socket.Receive(b, 1, SocketFlags.None);
                                // KEYEVENTF_KEYUP == 2
                                keybd_event(b[0], 0, 2, UIntPtr.Zero);
                                break;
                            }
                        default:
                            {
                                // 255 = heartbeat, just consume
                                break;
                            }
                    }
                }
                catch (Exception e)
                {
                    /*ifdef debug MessageBox.Show(e.Message);*/
                    cansenddifferential = false;
                }
            }
        }

        static void SendScreen()
        {
            //keybd_event(44, 0, 0, UIntPtr.Zero);  // printscr down
            //keybd_event(44, 0, 2, UIntPtr.Zero);  // printscr up
            //Image im = Clipboard.GetImage();

            if (quality == 10)  // PNG
            {
                if (cansenddifferential)
                {
                    Bitmap bm = null;
                    Bitmap bmfull = null;
                    if (hbmflipflop)
                    {
                        BitBlt(hdc1, 0, 0, bounds.Width, bounds.Height, hdcScreen, 0, 0, /*SRCCOPY*/0x00CC0020);
                        SelectObject(hdc2, hbmCursor);
                        TransparentBlt(hdc1, Cursor.Position.X, Cursor.Position.Y, 12, 21, hdc2, 0, 0, 12, 21, 0xFF00FF); /* cursor size */
                        SelectObject(hdc2, hbm2);
                        BitBlt(hdc2, 0, 0, bounds.Width, bounds.Height, hdc1, 0, 0, /*SRCINVERT*/0x00660046);

                        bmfull = Bitmap.FromHbitmap(hbm1);
                        bm = Bitmap.FromHbitmap(hbm2);
                    }
                    else
                    {
                        BitBlt(hdc2, 0, 0, bounds.Width, bounds.Height, hdcScreen, 0, 0, /*SRCCOPY*/0x00CC0020);
                        SelectObject(hdc1, hbmCursor);
                        TransparentBlt(hdc2, Cursor.Position.X, Cursor.Position.Y, 12, 21, hdc1, 0, 0, 12, 21, 0xFF00FF); /* cursor size */
                        SelectObject(hdc1, hbm1);
                        BitBlt(hdc1, 0, 0, bounds.Width, bounds.Height, hdc2, 0, 0, /*SRCINVERT*/0x00660046);

                        bmfull = Bitmap.FromHbitmap(hbm2);
                        bm = Bitmap.FromHbitmap(hbm1);
                    }
                    hbmflipflop = !hbmflipflop;

                    MemoryStream ms = new MemoryStream();
                    //tifparam.Param[0] = new EncoderParameter(Encoder.ColorDepth, 24L);
                    //bm.Save(ms, tifcodec, tifparam);
                    //bm.Dispose();
                    //bm = new Bitmap(ms);
                    //ms.Position = 0;
                    //ms.SetLength(0);
                    bm.Save(ms, ImageFormat.Png);
                    bm.Dispose();

                    MemoryStream ns = new MemoryStream();
                    //bmfull.Save(ns, tifcodec, tifparam);
                    //bmfull.Dispose();
                    //bmfull = new Bitmap(ns);
                    //ns.Position = 0;
                    //ns.SetLength(0);
                    bmfull.Save(ns, ImageFormat.Png);
                    bmfull.Dispose();

                    if (ms.Length > ns.Length)
                    {
                        // bitmapa určitě nebude větší než 0x01000000 B = 16 MB
                        // => bohatě stačí 3 bajty na length
                        // první bajt bude řikat jestli posílam differential bitmap
                        // 0 == full, 1 == differential
                        byte[] buff = new byte[4];
                        int l = (int)ns.Length;
                        buff[3] = (byte)l;
                        l = l >> 8;
                        buff[2] = (byte)l;
                        l = l >> 8;
                        buff[1] = (byte)l;
                        buff[0] = 0;
                        socket.Send(buff);
                        socket.Send(ns.ToArray());
                    }
                    else
                    {
                        byte[] buff = new byte[4];
                        int l = (int)ms.Length;
                        buff[3] = (byte)l;
                        l = l >> 8;
                        buff[2] = (byte)l;
                        l = l >> 8;
                        buff[1] = (byte)l;
                        buff[0] = 1;
                        socket.Send(buff);
                        socket.Send(ms.ToArray());
                    }

                    ms.Dispose();
                    ns.Dispose();
                }
                else  // can not send differential
                {
                    BitBlt(hdc1, 0, 0, bounds.Width, bounds.Height, hdcScreen, 0, 0, /*SRCCOPY*/0x00CC0020);
                    SelectObject(hdc2, hbmCursor);
                    TransparentBlt(hdc1, Cursor.Position.X, Cursor.Position.Y, 12, 21, hdc2, 0, 0, 12, 21, 0xFF00FF); /* cursor size */
                    SelectObject(hdc2, hbm2);
                    Bitmap bm = Bitmap.FromHbitmap(hbm1);
                    MemoryStream ms = new MemoryStream();
                    //tifparam.Param[0] = new EncoderParameter(Encoder.ColorDepth, 24L);
                    //bm.Save(ms, tifcodec, tifparam);
                    //bm.Dispose();
                    //bm = new Bitmap(ms);
                    //ms.Position = 0;
                    //ms.SetLength(0);
                    bm.Save(ms, ImageFormat.Png);
                    bm.Dispose();
                    cansenddifferential = true;
                    hbmflipflop = false;  // (hbmflipflop == true) === v hbm2 je previous

                    byte[] buff = new byte[4];
                    int l = (int)ms.Length;
                    buff[3] = (byte)l;
                    l = l >> 8;
                    buff[2] = (byte)l;
                    l = l >> 8;
                    buff[1] = (byte)l;
                    buff[0] = 0;
                    socket.Send(buff);
                    socket.Send(ms.ToArray());
                    ms.Dispose();
                }
            }
            else if (quality == 0)
            {
                // rozmyslet jestli chceme umožnit posílat differential pro quality 0
                // spíš ne
                BitBlt(hdc1, 0, 0, bounds.Width, bounds.Height, hdcScreen, 0, 0, /*SRCCOPY*/0x00CC0020);
                SelectObject(hdc2, hbmCursor);
                TransparentBlt(hdc1, Cursor.Position.X, Cursor.Position.Y, 12, 21, hdc2, 0, 0, 12, 21, 0xFF00FF); /* cursor size */
                SelectObject(hdc2, hbm2);
                Bitmap bm = Bitmap.FromHbitmap(hbm1);
                MemoryStream ms = new MemoryStream();
                //tifparam.Param[0] = new EncoderParameter(Encoder.ColorDepth, 1L);
                bm.Save(ms, tifcodec, tifparam);
                bm.Dispose();
                bm = new Bitmap(ms);
                ms.Position = 0;
                ms.SetLength(0);
                bm.Save(ms, ImageFormat.Png);
                bm.Dispose();
                cansenddifferential = false;

                byte[] buff = new byte[4];
                int l = (int)ms.Length;
                buff[3] = (byte)l;
                l = l >> 8;
                buff[2] = (byte)l;
                l = l >> 8;
                buff[1] = (byte)l;
                buff[0] = 0;
                socket.Send(buff);
                socket.Send(ms.ToArray());
                ms.Dispose();
            }
            else  // JPG
            {
                BitBlt(hdc1, 0, 0, bounds.Width, bounds.Height, hdcScreen, 0, 0, /*SRCCOPY*/0x00CC0020);
                SelectObject(hdc2, hbmCursor);
                TransparentBlt(hdc1, Cursor.Position.X, Cursor.Position.Y, 12, 21, hdc2, 0, 0, 12, 21, 0xFF00FF); /* cursor size */
                SelectObject(hdc2, hbm2);
                Bitmap bm = Bitmap.FromHbitmap(hbm1);
                MemoryStream ms = new MemoryStream();
                bm.Save(ms, jpgcodec, encparam);
                bm.Dispose();
                cansenddifferential = false;

                byte[] buff = new byte[4];
                int l = (int)ms.Length;
                buff[3] = (byte)l;
                l = l >> 8;
                buff[2] = (byte)l;
                l = l >> 8;
                buff[1] = (byte)l;
                buff[0] = 0;
                socket.Send(buff);
                socket.Send(ms.ToArray());
                ms.Dispose();
            }
        }
    }
}
