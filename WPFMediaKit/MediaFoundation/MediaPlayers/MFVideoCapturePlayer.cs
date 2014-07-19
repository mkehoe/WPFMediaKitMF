using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MediaFoundation.Misc;
using MediaFoundation.Controls;

namespace MediaFoundation.MediaPlayers
{
    public class VideoSampleArgs : EventArgs
    {
        public Bitmap VideoFrame { get; internal set; }
    }
    public class CaptureFormat
    {
        public int Height { get; set; }
        public int Width { get; set; }
        public float Framerate { get; set; }
        public int Index { get; set; }
        public Guid PixelFormat { get; set;}
        public String PixelFormatString
        {
            get
            {
                return getPixelFormatName(PixelFormat);
            }
        }

        public string getPixelFormatName(Guid guid)
        {
            if (guid.ToString().ToLower() == "32595559-0000-0010-8000-00aa00389b71")
            {
                return "YUY2";
            }
            else if (guid.ToString().ToLower() == "3231564e-0000-0010-8000-00aa00389b71")
            {
                return "YV12";
            }
            return "Unknown Format = " + guid.ToString();

        }

    }

    /// <summary>
    /// A Player that plays video from a video capture device.
    /// </summary>
    public class MFVideoCapturePlayer : MFMediaPlayerBase
    {
        //{f7e34c9a-42e8-4714-b74b-cb29d72c35e5} { }

        [DllImport("Kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, [MarshalAs(UnmanagedType.U4)] int length);

        #region Locals
        /// <summary>
        /// The video capture pixel height
        /// </summary>
        private int m_desiredHeight = 240;

        /// <summary>
        /// The video capture pixel width
        /// </summary>
        private int m_desiredWidth = 320;

        /// <summary>
        /// The video capture's frames per second
        /// </summary>
        private int m_fps = 30;

        /// <summary>
        /// The DirectShow video renderer
        /// </summary>
        //private IBaseFilter m_renderer;

        /// <summary>
        /// The capture device filter
        /// </summary>
        //private IBaseFilter m_captureDevice;

        /// <summary>
        /// The name of the video capture source device
        /// </summary>
        private string m_videoCaptureSource;

        /// <summary>
        /// Flag to detect if the capture source has changed
        /// </summary>
        private bool m_videoCaptureSourceChanged;

        /// <summary>
        /// The video capture device
        /// </summary>
        private MFDevice m_videoCaptureDevice = null;

        /// <summary>
        /// Flag to detect if the capture source device has changed
        /// </summary>
        private bool m_videoCaptureDeviceChanged;

        /// <summary>
        /// The sample grabber interface used for getting samples in a callback
        /// </summary>
        //private ISampleGrabber m_sampleGrabber;

        private string m_fileName;

#if DEBUG
        //private DsROTEntry m_rotEntry;
#endif
        #endregion

        /// <summary>
        /// Gets or sets if the instance fires an event for each of the samples
        /// </summary>
        public bool EnableSampleGrabbing { get; set; }

        /// <summary>
        /// Fires when a new video sample is ready
        /// </summary>
        public event EventHandler<VideoSampleArgs> NewVideoSample;

        private void InvokeNewVideoSample(VideoSampleArgs e)
        {
            EventHandler<VideoSampleArgs> sample = NewVideoSample;
            if (sample != null) sample(this, e);
        }

        /// <summary>
        /// The name of the video capture source to use
        /// </summary>
        public string VideoCaptureSource
        {
            get
            {
                VerifyAccess();
                return m_videoCaptureSource;
            }
            set
            {
                VerifyAccess();
                m_videoCaptureSource = value;
                m_videoCaptureSourceChanged = true;

                /* Free our unmanaged resources when
                 * the source changes */
                FreeResources();
            }
        }

        public MFDevice VideoCaptureDevice
        {
            get
            {
                VerifyAccess();
                return m_videoCaptureDevice;
            }
            set
            {
                VerifyAccess();
                m_videoCaptureDevice = value;
                m_videoCaptureDeviceChanged = true;

                /* Free our unmanaged resources when
                 * the source changes */
                FreeResources();
            }
        }

        /// <summary>
        /// The frames per-second to play
        /// the capture device back at
        /// </summary>
        public int FPS
        {
            get
            {
                VerifyAccess();
                return m_fps;
            }
            set
            {
                VerifyAccess();

                /* We support only a minimum of
                 * one frame per second */
                if (value < 1)
                    value = 1;

                m_fps = value;
            }
        }

        /// <summary>
        /// Gets or sets if Yuv is the prefered color space
        /// </summary>
        public bool UseYuv { get; set; }

        /// <summary>
        /// The desired pixel width of the video
        /// </summary>
        public int DesiredWidth
        {
            get
            {
                VerifyAccess();
                return m_desiredWidth;
            }
            set
            {
                VerifyAccess();
                m_desiredWidth = value;
            }
        }

        /// <summary>
        /// The desired pixel height of the video
        /// </summary>
        public int DesiredHeight
        {
            get
            {
                VerifyAccess();
                return m_desiredHeight;
            }
            set
            {
                VerifyAccess();
                m_desiredHeight = value;
            }
        }

        public string FileName
        {
            get
            {
                //VerifyAccess();
                return m_fileName;
            }
            set
            {
                //VerifyAccess();
                m_fileName = value;
            }
        }

        /// <summary>
        /// Plays the video capture device
        /// </summary>
        public override void Play()
        {
            VerifyAccess();

            if (m_pSession == null)
                SetupGraph();

            base.Play();
        }

        /// <summary>
        /// Pauses the video capture device
        /// </summary>
        public override void Pause()
        {
            VerifyAccess();

            //if (m_graph == null)
            //    SetupGraph();

            //base.Pause();
        }

        public void ShowCapturePropertyPages(IntPtr hwndOwner)
        {
           
        }
        private void CreateVideoCaptureSource()
        {
            try
            {
                if (VideoCaptureDevice == null)
                {
                    Trace.WriteLine("Error no videocapturedevice set");
                    return;
                }
                //VideoCaptureSource
                IMFAttributes pAttributes = null;
                int hr = MFExtern.MFCreateAttributes(out pAttributes, 2);

                hr = pAttributes.SetGUID(MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetString(MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, VideoCaptureDevice.DevicePath);
                MFError.ThrowExceptionForHR(hr);

                IMFMediaSource ppMediaSource = null;
                hr = MFExtern.MFCreateDeviceSource(pAttributes, out ppMediaSource);
                MFError.ThrowExceptionForHR(hr);

                m_pSource = ppMediaSource;
                //GetCaptureFormats(m_pSource);
            }
            catch (Exception e)
            {
                Marshal.GetHRForException(e);
                Trace.WriteLine("SetupGraph Exception " + e.ToString());
            }
        }
        /// <summary>
        /// Configures the DirectShow graph to play the selected video capture
        /// device with the selected parameters
        /// </summary>
        private void SetupGraph()
        {
            /* Clean up any messes left behind */
            FreeResources();
            Trace.WriteLine("setting up graph 1");
            try
            {
                Trace.WriteLine("setting up graph 2");
                // Create the media session.
                CreateSession();
                Trace.WriteLine("setting up graph 3");

                CreateVideoCaptureSource();

                // Create a partial topology.
                IMFTopology pTopology = CreateTopologyFromSource();

                Trace.WriteLine("setting up graph 4");
                // Set the topology on the media session.
                int hr = m_pSession.SetTopology(0, pTopology);
                MFError.ThrowExceptionForHR(hr);

                //m_pSession.GetClock(out pClock);
                Trace.WriteLine("setting up graph 5");
                COMBase.SafeRelease(pTopology);
                
                
                //Marshal.ReleaseComObject();
            }
            catch (Exception ex)
            {
                Marshal.GetHRForException(ex);
                
                Trace.WriteLine("SetupGraph Exception " + ex.ToString());
                FreeResources();
                InvokeMediaFailed(new MediaFailedEventArgs(ex.Message, ex));
            }

            /* Success */
            InvokeMediaOpened();
        }
        public void SetDeviceFormat(int dwFormatIndex)
        {
            if (m_pSource == null)
            {
                CreateVideoCaptureSource();
            }
            IMFPresentationDescriptor pPD = null;
            IMFStreamDescriptor pSD = null;
            IMFMediaTypeHandler pHandler = null;
            IMFMediaType pType = null;

            int hr = m_pSource.CreatePresentationDescriptor(out pPD);
            MFError.ThrowExceptionForHR(hr);

            bool fSelected;
            hr = pPD.GetStreamDescriptorByIndex(0, out fSelected, out pSD);    
            MFError.ThrowExceptionForHR(hr);

            hr = pSD.GetMediaTypeHandler(out pHandler);
            MFError.ThrowExceptionForHR(hr);

            hr = pHandler.GetMediaTypeByIndex(dwFormatIndex, out pType);
            MFError.ThrowExceptionForHR(hr);

            hr = pHandler.SetCurrentMediaType(pType);
            MFError.ThrowExceptionForHR(hr);

        
            Marshal.FinalReleaseComObject(pPD);
            Marshal.FinalReleaseComObject(pSD);
            Marshal.FinalReleaseComObject(pHandler);
            Marshal.FinalReleaseComObject(pType);
        
        }
        public CaptureFormat[] GetCaptureFormats()
        {
            if (m_pSource == null)
            {
                CreateVideoCaptureSource();
            }
            IMFPresentationDescriptor pPD = null;
            IMFStreamDescriptor pSD = null;
            IMFMediaTypeHandler pHandler = null;
            IMFMediaType pType = null;

            int hr = m_pSource.CreatePresentationDescriptor(out pPD);
            MFError.ThrowExceptionForHR(hr);
            
            bool fSelected;
            hr = pPD.GetStreamDescriptorByIndex(0, out fSelected, out pSD);
            MFError.ThrowExceptionForHR(hr);
            
            hr = pSD.GetMediaTypeHandler(out pHandler);
            MFError.ThrowExceptionForHR(hr);
            
            int cTypes = 0;
            hr = pHandler.GetMediaTypeCount(out cTypes);
            MFError.ThrowExceptionForHR(hr);
            
            CaptureFormat[] captureFormats = new CaptureFormat[cTypes];

            for (int i = 0; i < cTypes; i++)
            {
                hr = pHandler.GetMediaTypeByIndex(i, out pType);
                MFError.ThrowExceptionForHR(hr);

                
                CaptureFormat mediatype = LogMediaType(pType);
                Trace.WriteLine(mediatype);
                Trace.WriteLine("Media Type " +i.ToString());
                captureFormats[i] = mediatype;

                //OutputDebugString(L"\n");
                Marshal.FinalReleaseComObject(pType);
                
            }

            Marshal.FinalReleaseComObject(pPD);
            Marshal.FinalReleaseComObject(pSD);
            Marshal.FinalReleaseComObject(pHandler);
            Marshal.FinalReleaseComObject(pType);
            return captureFormats;
            
        }

        private CaptureFormat LogMediaType(IMFMediaType pType)
        {
            int count = 0;
            
            int hr = pType.GetCount(out count);
            MFError.ThrowExceptionForHR(hr);

            if (count == 0)
            {
                Trace.WriteLine("Empty media type.");
            }
            CaptureFormat capFormat = new CaptureFormat();
            for (int i = 0; i < count; i++)
            {
                LogAttributeValueByIndex(pType, i, ref capFormat);
                MFError.ThrowExceptionForHR(hr);
            }
            return capFormat;
            
        }
        private string LogAttributeValueByIndex(IMFAttributes pAttr, int index, ref CaptureFormat capFormat)
        {
            //string pGuidName = null;
            //string pGuidValName = null;
            
            Guid guid = Guid.Empty;

            PropVariant var = new PropVariant();
            //PropVariantInit(&var);

            
            int hr = pAttr.GetItemByIndex(index, out guid, var);
            MFError.ThrowExceptionForHR(hr);

            
            
            if ( guid == MFAttributesClsid.MF_MT_FRAME_RATE)
            {
                //Trace.WriteLine("MF_MT_FRAME_RATE");
                int lower = (int)var.GetULong();
                //Trace.WriteLine("LogAttr1_1 = " + lower.ToString());
                int upperbits = (int)(var.GetULong() >> 32);
                //Trace.WriteLine("LogAttr1_2 = " + upperbits.ToString());
                float fr = (float)upperbits / (float)lower;
                capFormat.Framerate = fr;
                Trace.WriteLine("FrameRate = " + fr.ToString());
                return "Framerate=" + fr.ToString() + ", ";
            }
            else if ( guid ==MFAttributesClsid.MF_MT_FRAME_RATE_RANGE_MAX)
            {
                //Trace.WriteLine("MF_MT_FRAME_RATE_RANGE_MAX");
                int lower = (int)var.GetULong();
                //Trace.WriteLine("LogAttr1_1 = " + lower.ToString());
                int upperbits = (int)(var.GetULong() >> 32);
                //Trace.WriteLine("LogAttr1_2 = " + upperbits.ToString());
                //return "MaxFramerate=" + fr.ToString() + ", ";
            }
            else if (guid == MFAttributesClsid.MF_MT_FRAME_RATE_RANGE_MIN)
            {
                //Trace.WriteLine("MF_MT_FRAME_RATE_RANGE_MIN");
                int lower = (int)var.GetULong();
                //Trace.WriteLine("LogAttr1_1 = " + lower.ToString());
                int upperbits = (int)(var.GetULong() >> 32);
                float fr = (float)upperbits / (float)lower;
                //return "MinFramerate=" + fr.ToString() + ", ";
                //Trace.WriteLine("LogAttr1_2 = " + upperbits.ToString());
            }
            else if (guid == MFAttributesClsid.MF_MT_FRAME_SIZE)
            {
                //Trace.WriteLine("MF_MT_FRAME_SIZE");
                int lower = (int)var.GetULong();
                // Trace.WriteLine("LogAttr1_1 = " + lower.ToString());
                int upperbits = (int)(var.GetULong() >> 32);
                //Trace.WriteLine("LogAttr1_2 = " + upperbits.ToString());
                capFormat.Height = lower;
                capFormat.Width = upperbits;
                Trace.WriteLine("Resolution=" +lower.ToString() + "X" + upperbits.ToString());
                return "Resolution=" +lower.ToString() + "X" + upperbits.ToString();
            }
            else if (guid == MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO)
            {
                //Trace.WriteLine("MF_MT_PIXEL_ASPECT_RATIO");
                int lower = (int)var.GetULong();
                //Trace.WriteLine("LogAttr1_1 = " + lower.ToString());
                int upperbits = (int)(var.GetULong() >> 32);
                //Trace.WriteLine("LogAttr1_2 = " + upperbits.ToString());
            }
            else if (guid == MFAttributesClsid.MF_MT_GEOMETRIC_APERTURE)
            {
                //Trace.WriteLine("MF_MT_GEOMETRIC_APERTURE");
                //Trace.WriteLine("LogAttr2 = " + var.ToString());        
            }
            else if (guid == MFAttributesClsid.MF_MT_MINIMUM_DISPLAY_APERTURE)
            {
                //Trace.WriteLine("MF_MT_MINIMUM_DISPLAY_APERTURE");
                //Trace.WriteLine("LogAttr2 = " + var.ToString());        
            }
            else if (guid == MFAttributesClsid.MF_MT_PAN_SCAN_APERTURE)
            {
                //Trace.WriteLine("MF_MT_PAN_SCAN_APERTURE");
                //Trace.WriteLine("LogAttr2 = " + var.ToString());        
            }
            else if (guid == MFAttributesClsid.MF_MT_SUBTYPE)
            {
                capFormat.PixelFormat = var.GetGuid();
            }
            else
            {
                Trace.WriteLine("Unknown attr " + guid.ToString() + " val = " + var.ToString());

            }
            return "";

        }

        /// <summary>
        /// Sets the capture parameters for the video capture device
        /// </summary>
        private bool SetVideoCaptureParameters(/*ICaptureGraphBuilder2 capGraph, IBaseFilter captureFilter, Guid mediaSubType*/)
        {
            return true;
        }

        private Bitmap m_videoFrame;

        private void InitializeBitmapFrame(int width, int height)
        {
            if (m_videoFrame != null)
            {
                m_videoFrame.Dispose();
            }

            m_videoFrame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        }

        protected override void FreeResources()
        {
            /* We run the StopInternal() to avoid any 
             * Dispatcher VeryifyAccess() issues */
            StopInternal();

            /* Let's clean up the base 
             * class's stuff first */
            base.FreeResources();

            if (m_videoFrame != null)
            {
                m_videoFrame.Dispose();
                m_videoFrame = null;
            }
            
            if (m_pSource != null)
            {
                Marshal.FinalReleaseComObject(m_pSource);
                m_pSource = null;
            }

            if (m_pSession != null)
            {
                Marshal.FinalReleaseComObject(m_pSession);
                m_pSession = null;
                InvokeMediaClosed(new EventArgs());
            }
            
        }
    }
}