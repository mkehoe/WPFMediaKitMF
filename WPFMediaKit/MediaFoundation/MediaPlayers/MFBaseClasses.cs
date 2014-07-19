#region Includes
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
//using DirectShowLib;
using MediaFoundation.MediaPlayers.EVR;
//using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.EVR;
using MediaFoundation.Transform;
using WPFMediaKit.Threading;
using Size = System.Windows.Size;
#endregion

namespace MediaFoundation.MediaPlayers
{

    
    public enum MediaState
    {
        Manual,
        Play,
        Stop,
        Close,
        Pause
    }

    /// <summary>
    /// The types of position formats that
    /// are available for seeking media
    /// </summary>
    public enum MediaPositionFormat
    {
        MediaTime,
        Frame,
        Byte,
        Field,
        Sample,
        None
    }

    /// <summary>
    /// Delegate signature to notify of a new surface
    /// </summary>
    /// <param name="sender">The sender of the event</param>
    /// <param name="pSurface">The pointer to the D3D surface</param>
    public delegate void NewAllocatorSurfaceDelegate(object sender, IntPtr pSurface);

    /// <summary>
    /// The arguments that store information about a failed media attempt
    /// </summary>
    public class MediaFailedEventArgs : EventArgs
    {
        public MediaFailedEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }

        public Exception Exception { get; protected set; }
        public string Message { get; protected set; }
    }

    /// <summary>
    /// The custom allocator interface.  All custom allocators need
    /// to implement this interface.
    /// </summary>
    public interface ICustomAllocator : IDisposable
    {
        /// <summary>
        /// Invokes when a new frame has been allocated
        /// to a surface
        /// </summary>
        event Action NewAllocatorFrame;

        /// <summary>
        /// Invokes when a new surface has been allocated
        /// </summary>
        event NewAllocatorSurfaceDelegate NewAllocatorSurface;
    }

    [ComImport, Guid("FA10746C-9B63-4b6c-BC49-FC300EA5F256")]
    internal class EnhancedVideoRenderer
    {
    }

    /// <summary>
    /// A low level window class that is used to provide interop with libraries
    /// that require an hWnd 
    /// </summary>
    public class HiddenWindow : NativeWindow
    {
        public delegate IntPtr WndProcHookDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

        readonly List<WndProcHookDelegate> m_handlerlist = new List<WndProcHookDelegate>();

        public void AddHook(WndProcHookDelegate method)
        {
            if (m_handlerlist.Contains(method))
                return;

            lock (((System.Collections.ICollection)m_handlerlist).SyncRoot)
                m_handlerlist.Add(method);
        }

        public void RemoveHook(WndProcHookDelegate method)
        {
            lock (((System.Collections.ICollection)m_handlerlist).SyncRoot)
                m_handlerlist.Remove(method);
        }

        /// <summary>
        /// Invokes the windows procedure associated to this window
        /// </summary>
        /// <param name="m">The window message to send to window</param>
        protected override void WndProc(ref Message m)
        {
            bool isHandled = false;

            lock (((System.Collections.ICollection)m_handlerlist).SyncRoot)
            {
                foreach (WndProcHookDelegate method in m_handlerlist)
                {
                    method.Invoke(m.HWnd, m.Msg, m.WParam, m.LParam, ref isHandled);
                    if (isHandled)
                        break;
                }
            }

            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// The MediaPlayerBase is a base class to build raw, DirectShow based players.
    /// It inherits from DispatcherObject to allow easy communication with COM objects
    /// from different apartment thread models.
    /// </summary>
    public abstract class MFMediaPlayerBase : WorkDispatcherObject
    {
        [DllImport("user32.dll", SetLastError = false)]
        private static extern IntPtr GetDesktopWindow();

        
        //private object CustomEVR;
        //Guid EVR_GUID = new Guid("FA10746C-9B63-4b6c-BC49-FC300EA5F256");

        /// <summary>
        /// One second in 100ns units
        /// </summary>
        protected const long DSHOW_ONE_SECOND_UNIT = 10000000;

        /// <summary>
        /// Rate which our DispatcherTimer polls the graph
        /// </summary>
        private const int DSHOW_TIMER_POLL_MS = 33;

        /// <summary>
        /// UserId value for the VMR9 Allocator - Not entirely useful
        /// for this application of the VMR
        /// </summary>
        private readonly IntPtr m_userId = new IntPtr(unchecked((int)0xDEADBEEF));

        /// <summary>
        /// Static lock.  Seems multiple EVR controls instantiated at the same time crash
        /// </summary>
        private static readonly object m_videoRendererInitLock = new object();

        /// <summary>
        /// The custom DirectShow allocator
        /// </summary>
        private ICustomAllocator m_customAllocator;

        /// <summary>
        /// The hWnd pointer we use for D3D stuffs
        /// </summary>
        private HiddenWindow m_window;

        /// <summary>
        /// Flag for if our media has video
        /// </summary>
        private bool m_hasVideo;

        /// <summary>
        /// The natural video pixel height, if applicable
        /// </summary>
        private int m_naturalVideoHeight;

        /// <summary>
        /// The natural video pixel width, if applicable
        /// </summary>
        private int m_naturalVideoWidth;

        /// <summary>
        /// Our Win32 timer to poll the DirectShow graph
        /// </summary>
        private System.Timers.Timer m_timer;

        protected IMFMediaSession m_pSession = null;
        protected IMFMediaSource m_pSource = null;
        
        protected IMFAsyncCallback OnOpenURL = null;
        protected IMFMediaSink StreamingAudioRenderer;
        //protected IMFMediaSession MediaSession;
        public IMFAudioStreamVolume SimpleAudioVolume = null;// todo saferelease

        /// <summary>
        /// This objects last stand
        /// </summary>
        ~MFMediaPlayerBase()
        {
            int hr = MFExtern.MFShutdown();
            MFError.ThrowExceptionForHR(hr);
            Dispose();
        }
        public MFMediaPlayerBase()
        {
            int hr = MFExtern.MFStartup(0x10070, MFStartup.Full);
            MFError.ThrowExceptionForHR(hr);
        }
      
        /// <summary>
        /// Helper function to get a valid hWnd to
        /// use with DirectShow and Direct3D
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        private void GetMainWindowHwndHelper()
        {
            if (m_window == null)
                m_window = new HiddenWindow();
            else
                return;

            if (m_window.Handle == IntPtr.Zero)
            {
                lock (m_window)
                {
                    m_window.CreateHandle(new CreateParams());
                }
            }
        }

        protected virtual HiddenWindow HwndHelper
        {
            get
            {
                if (m_window != null)
                    return m_window;

                GetMainWindowHwndHelper();

                return m_window;
            }
        }

        /// <summary>
        /// Is true if the media contains renderable video
        /// </summary>
        public virtual bool HasVideo
        {
            get
            {
                return m_hasVideo;
            }
            protected set
            {
                m_hasVideo = value;
            }
        }

        /// <summary>
        /// Gets the natural pixel width of the current media.
        /// The value will be 0 if there is no video in the media.
        /// </summary>
        public virtual int NaturalVideoWidth
        {
            get
            {
                VerifyAccess();
                return m_naturalVideoWidth;
            }
            protected set
            {
                VerifyAccess();
                m_naturalVideoWidth = value;
            }
        }

        /// <summary>
        /// Gets the natural pixel height of the current media.  
        /// The value will be 0 if there is no video in the media.
        /// </summary>
        public virtual int NaturalVideoHeight
        {
            get
            {
                VerifyAccess();
                return m_naturalVideoHeight;
            }
            protected set
            {
                VerifyAccess();
                m_naturalVideoHeight = value;
            }
        }
        
        /// <summary>
        /// Gets or sets the audio volume.  Specifies the volume, as a 
        /// number from 0 to 1.  Full volume is 1, and 0 is silence.
        /// </summary>
        public virtual double Volume
        {
            get
            {
                int hr = 0;
                VerifyAccess();

                if (SimpleAudioVolume == null)
                    return 0;
                float m_fVol = 0.0F;
                //hr = SimpleAudioVolume.GetAllVolumes(out m_fVol);
                MFError.ThrowExceptionForHR(hr);
                return (double)m_fVol;
            }
            set
            {
                //int hr = 0;
                VerifyAccess();

                if (SimpleAudioVolume == null)
                    return;
                
                float newVol = (float)value;
                int chcount = 0;
                SimpleAudioVolume.GetChannelCount(out chcount);
                SimpleAudioVolume.SetAllVolumes(chcount, ref newVol);
            }
        }

        /// <summary>
        /// Event notifies when there is a new video frame
        /// to be rendered
        /// </summary>
        public event Action NewAllocatorFrame;

        /// <summary>
        /// Event notifies when there is a new surface allocated
        /// </summary>
        public event NewAllocatorSurfaceDelegate NewAllocatorSurface;

        /// <summary>
        /// Frees any remaining memory
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            //GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Part of the dispose pattern
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            
            if (!disposing)
                return;

            if (m_window != null)
            {
                m_window.RemoveHook(WndProcHook);
                m_window.DestroyHandle();
                m_window = null;
            }

            if (m_timer != null)
                m_timer.Dispose();

            m_timer = null;

            if (CheckAccess())
            {
                FreeResources();
                Dispatcher.BeginInvokeShutdown();
            }
            else
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    FreeResources();
                    Dispatcher.BeginInvokeShutdown();
                });
            }
        }

        /// <summary>
        /// Polls the graph for various data about the media that is playing
        /// </summary>
        protected virtual void OnTimerTick()
        {
        }

        /// <summary>
        /// Is called when a new media event code occurs in the session
        /// </summary>
        /// <param name="code">The event code that occured</param>
        /// <param name="param1">The first parameter sent by the graph</param>
        /// <param name="param2">The second parameter sent by the graph</param>
        protected virtual void OnMediaEvent(IMFMediaEvent code)
        {
            MediaEventType MedEventType;
            code.GetType(out MedEventType);
            //Trace.WriteLine("Session Event " + MedEventType.ToString());
            switch (MedEventType)
            {
                case MediaEventType.MEEndOfStream:
                    InvokeMediaEnded(null);
                    StopGraphPollTimer();
                    break;
                case MediaEventType.MESessionPaused:
                    break;
                case MediaEventType.MESessionTopologySet:
                    AddAudioInterface();
                    break;
                case MediaEventType.MESessionClosed:
                case MediaEventType.MESessionEnded:
                case MediaEventType.MEEndOfPresentation:
                case MediaEventType.MEError:
                case MediaEventType.MESessionNotifyPresentationTime:
                case MediaEventType.MESessionStarted:
                default:
                    break;
            }
            //COMBase.SafeRelease(MedEventType);
        }

        /// <summary>
        /// Starts the graph polling timer to update possibly needed
        /// things like the media position
        /// </summary>
        protected void StartGraphPollTimer()
        {
            if (m_timer == null)
            {
                m_timer = new System.Timers.Timer();
                m_timer.Interval = DSHOW_TIMER_POLL_MS;
                m_timer.Elapsed += TimerElapsed;
            }

            m_timer.Enabled = true;

            /* Make sure we get windows messages */
            //AddWndProcHook();
        }
        
        private void ProcesssMediaEvents()
        {
            Dispatcher.BeginInvoke((Action)delegate
            {
                if (m_pSession != null)
                {
                    IMFMediaEvent pmet;
                    
                    MFEventFlag flags = MFEventFlag.NoWait;
                    /* Get all the queued events from the interface */

                    while (m_pSession.GetEvent(flags, out pmet) == 0)
                    {
                        /* Handle anything for this event code */
                        OnMediaEvent(pmet);
                    }

                }
            });
        }
        protected void CreateSession()
        {
            // Close the old session, if any.
            CloseSession();

            // Create the media session.
            int hr = MFExtern.MFCreateMediaSession(null, out m_pSession);
            MFError.ThrowExceptionForHR(hr);
        }
        protected void CloseSession()
        {
            int hr;

            /*if (m_pVideoDisplay != null)
            {
                Marshal.ReleaseComObject(m_pVideoDisplay);
                m_pVideoDisplay = null;
            }
            */
            if (m_pSession != null)
            {
                hr = m_pSession.Close();
                MFError.ThrowExceptionForHR(hr);

                // Wait for the close operation to complete
                /*bool res = m_hCloseEvent.WaitOne(5000, true);
                if (!res)
                {
                    TRACE(("WaitForSingleObject timed out!"));
                }*/
            }

            // Complete shutdown operations

            // 1. Shut down the media source
            if (m_pSource != null)
            {
                hr = m_pSource.Shutdown();
                MFError.ThrowExceptionForHR(hr);
                COMBase.SafeRelease(m_pSource);
                m_pSource = null;
            }

            // 2. Shut down the media session. (Synchronous operation, no events.)
            if (m_pSession != null)
            {
                hr = m_pSession.Shutdown();
                Marshal.ReleaseComObject(m_pSession);
                m_pSession = null;
            }
        }
        protected IMFTopology CreateTopologyFromSource()
        {
            int hr = 0;

            //TRACE("CPlayer::CreateTopologyFromSource");

            //Assert(m_pSession != null);
            //Debug.Assert(m_pSource != null);

            IMFTopology pTopology = null;
            IMFPresentationDescriptor pSourcePD = null;
            int cSourceStreams = 0;

            try
            {
                // Create a new topology.
                hr = MFExtern.MFCreateTopology(out pTopology);
                MFError.ThrowExceptionForHR(hr);

                // Create the presentation descriptor for the media source.
                hr = m_pSource.CreatePresentationDescriptor(out pSourcePD);
                MFError.ThrowExceptionForHR(hr);

                // Get the number of streams in the media source.
                hr = pSourcePD.GetStreamDescriptorCount(out cSourceStreams);
                MFError.ThrowExceptionForHR(hr);

                //TRACE(string.Format("Stream count: {0}", cSourceStreams));

                // For each stream, create the topology nodes and add them to the topology.
                for (int i = 0; i < cSourceStreams; i++)
                {
                    AddBranchToPartialTopology(pTopology, pSourcePD, i);
                }

            }
            catch
            {
                // If we failed, release the topology
                COMBase.SafeRelease(pTopology);
                throw;
            }
            finally
            {
                COMBase.SafeRelease(pSourcePD);
            }
            return pTopology;
        }
        protected IMFTopologyNode CreateOutputNode(IMFStreamDescriptor pSourceSD)
        {
            IMFTopologyNode pNode = null;
            IMFMediaTypeHandler pHandler = null;
            IMFActivate pRendererActivate = null;

            Guid guidMajorType = Guid.Empty;
            int hr = 0;

            // Get the stream ID.
            int streamID = 0;

            try
            {
                try
                {
                    hr = pSourceSD.GetStreamIdentifier(out streamID); // Just for debugging, ignore any failures.
                    MFError.ThrowExceptionForHR(hr);
                }
                catch
                {
                    //TRACE("IMFStreamDescriptor::GetStreamIdentifier" + hr.ToString());
                }

                // Get the media type handler for the stream.
                hr = pSourceSD.GetMediaTypeHandler(out pHandler);
                MFError.ThrowExceptionForHR(hr);

                // Get the major media type.
                hr = pHandler.GetMajorType(out guidMajorType);
                MFError.ThrowExceptionForHR(hr);

                // Create a downstream node.
                hr = MFExtern.MFCreateTopologyNode(MFTopologyType.OutputNode, out pNode);
                MFError.ThrowExceptionForHR(hr);

                // Create an IMFActivate object for the renderer, based on the media type.
                if (MFMediaType.Audio == guidMajorType)
                {
                    // Create the audio renderer.
                    hr = MFExtern.MFCreateAudioRendererActivate(out pRendererActivate);
                    MFError.ThrowExceptionForHR(hr);
                    object sar;
                    pRendererActivate.ActivateObject(typeof(IMFMediaSink).GUID, out sar);
                    StreamingAudioRenderer = sar as IMFMediaSink;
                }
                else if (MFMediaType.Video == guidMajorType)
                {
                    // Create the video renderer.
                    pRendererActivate = CreateVideoRenderer();
                }
                else
                {
                    //TRACE(string.Format("Stream {0}: Unknown format", streamID));
                    throw new COMException("Unknown format");
                }

                // Set the IActivate object on the output node.
                hr = pNode.SetObject(pRendererActivate);
                MFError.ThrowExceptionForHR(hr);

            }
            catch(Exception ex)
            {
                // If we failed, release the pNode
                COMBase.SafeRelease(pNode);
                throw;
            }
            finally
            {
                // Clean up.
                COMBase.SafeRelease(pHandler);
                COMBase.SafeRelease(pRendererActivate);
            }
            return pNode;
        }
        protected void AddBranchToPartialTopology(IMFTopology pTopology, IMFPresentationDescriptor pSourcePD, int iStream)
        {
            int hr;

            //TRACE("CPlayer::AddBranchToPartialTopology");

            //Debug.Assert(pTopology != null);

            IMFStreamDescriptor pSourceSD = null;
            IMFTopologyNode pSourceNode = null;
            IMFTopologyNode pOutputNode = null;
            bool fSelected = false;

            try
            {
                // Get the stream descriptor for this stream.
                hr = pSourcePD.GetStreamDescriptorByIndex(iStream, out fSelected, out pSourceSD);
                MFError.ThrowExceptionForHR(hr);

                // Create the topology branch only if the stream is selected.
                // Otherwise, do nothing.
                if (fSelected)
                {
                    // Create a source node for this stream.
                    pSourceNode = CreateSourceStreamNode(pSourcePD, pSourceSD);

                    

                    // Add both nodes to the topology.
                    hr = pTopology.AddNode(pSourceNode);
                    MFError.ThrowExceptionForHR(hr);
                    
                    // Create the output node for the renderer.
                    pOutputNode = CreateOutputNode(pSourceSD);
                    if (pOutputNode == null)
                    {
                        throw new Exception("Could not create output node");
                    }
                    hr = pTopology.AddNode(pOutputNode);
                    MFError.ThrowExceptionForHR(hr);

                    // Connect the source node to the output node.
                    hr = pSourceNode.ConnectOutput(0, pOutputNode, 0);
                    MFError.ThrowExceptionForHR(hr);
                }
            }
            finally
            {
                // Clean up.
                COMBase.SafeRelease(pSourceSD);
                COMBase.SafeRelease(pSourceNode);
                COMBase.SafeRelease(pOutputNode);
            }
        }
        protected IMFTopologyNode CreateSourceStreamNode(IMFPresentationDescriptor pSourcePD, IMFStreamDescriptor pSourceSD)
        {
            int hr;
            //Debug.Assert(m_pSource != null);

            IMFTopologyNode pNode = null;

            try
            {
                // Create the source-stream node.
                hr = MFExtern.MFCreateTopologyNode(MFTopologyType.SourcestreamNode, out pNode);
                MFError.ThrowExceptionForHR(hr);

                // Set attribute: Pointer to the media source.
                hr = pNode.SetUnknown(MFAttributesClsid.MF_TOPONODE_SOURCE, m_pSource);
                MFError.ThrowExceptionForHR(hr);

                // Set attribute: Pointer to the presentation descriptor.
                hr = pNode.SetUnknown(MFAttributesClsid.MF_TOPONODE_PRESENTATION_DESCRIPTOR, pSourcePD);
                MFError.ThrowExceptionForHR(hr);

                // Set attribute: Pointer to the stream descriptor.
                hr = pNode.SetUnknown(MFAttributesClsid.MF_TOPONODE_STREAM_DESCRIPTOR, pSourceSD);
                MFError.ThrowExceptionForHR(hr);

                // Return the IMFTopologyNode pointer to the caller.
                return pNode;
            }
            catch
            {
                // If we failed, release the pnode
                COMBase.SafeRelease(pNode);
                throw;
            }
        }
        private void TimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)delegate
            {
                ProcesssMediaEvents();
                //OnGraphTimerTick();
                OnTimerTick();
            });
        }

        /// <summary>
        /// Stops the graph polling timer
        /// </summary>
        protected void StopGraphPollTimer()
        {
            if (m_timer != null)
            {
                m_timer.Stop();
                m_timer.Dispose();
                m_timer = null;
            }

            /* Stop listening to windows messages */
            //RemoveWndProcHook();
        }

        /// <summary>
        /// Receives windows messages.  This is primarily used to get
        /// events that happen on our graph
        /// </summary>
        /// <param name="hwnd">The window handle</param>
        /// <param name="msg">The message Id</param>
        /// <param name="wParam">The message's wParam value</param>
        /// <param name="lParam">The message's lParam value</param>
        /// <param name="handled">A value that indicates whether the message was handled. Set the value to true if the message was handled; otherwise, false. </param>
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            ProcesssMediaEvents();

            return IntPtr.Zero;
        }

        /// <summary>
        /// Unhooks the IMediaEventEx from the notification hWnd
        /// </summary>
        private void UnsetMediaEventExNotifyWindow()
        {
            
        }

        /// <summary>
        /// Notifies when the media has successfully been opened
        /// </summary>
        public event Action MediaOpened;

        /// <summary>
        /// Notifies when the media has been closed
        /// </summary>
        public event Action MediaClosed;

        /// <summary>
        /// Notifies when the media has failed and produced an exception
        /// </summary>
        public event EventHandler<MediaFailedEventArgs> MediaFailed;

        /// <summary>
        /// Notifies when the media has completed
        /// </summary>
        public event Action MediaEnded;

        /// <summary>
        /// Registers the custom allocator and hooks into it's supplied events
        /// </summary>
        protected void RegisterCustomAllocator(ICustomAllocator allocator)
        {
            FreeCustomAllocator();

            if (allocator == null)
                return;

            m_customAllocator = allocator;

            m_customAllocator.NewAllocatorFrame += CustomAllocatorNewAllocatorFrame;
            m_customAllocator.NewAllocatorSurface += CustomAllocatorNewAllocatorSurface;
        }

        /// <summary>
        /// Local event handler for the custom allocator's new surface event
        /// </summary>
        private void CustomAllocatorNewAllocatorSurface(object sender, IntPtr pSurface)
        {
            InvokeNewAllocatorSurface(pSurface);
        }

        /// <summary>
        /// Local event handler for the custom allocator's new frame event
        /// </summary>
        private void CustomAllocatorNewAllocatorFrame()
        {
            InvokeNewAllocatorFrame();
        }

        /// <summary>
        /// Disposes of the current allocator
        /// </summary>
        protected void FreeCustomAllocator()
        {
            if (m_customAllocator == null)
                return;

            m_customAllocator.Dispose();

            m_customAllocator.NewAllocatorFrame -= CustomAllocatorNewAllocatorFrame;
            m_customAllocator.NewAllocatorSurface -= CustomAllocatorNewAllocatorSurface;

            if (Marshal.IsComObject(m_customAllocator))
                Marshal.ReleaseComObject(m_customAllocator);

            m_customAllocator = null;
        }

        /// <summary>
        /// Resets the local graph resources to their
        /// default settings
        /// </summary>
        private void ResetGraphResources()
        {
            //m_graph = null;

            if (m_pSource != null)
                Marshal.ReleaseComObject(m_pSource);
            m_pSource = null;

            if (m_pSession != null)
                Marshal.ReleaseComObject(m_pSession);
            m_pSession = null;
        }

        /// <summary>
        /// Frees any allocated or unmanaged resources
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        protected virtual void FreeResources()
        {
            StopGraphPollTimer();
            ResetGraphResources();
            FreeCustomAllocator();
        }
        protected void AddAudioInterface()
        {
            if (m_pSession == null || StreamingAudioRenderer == null)
                return; //bad

            if (SimpleAudioVolume == null)
            {
                object vol = null;
                //MFExtern.MFGetService(m_pSession as object, MFServices.MR_POLICY_VOLUME_SERVICE, typeof(IMFSimpleAudioVolume).GUID, out vol);
                MFExtern.MFGetService(StreamingAudioRenderer, MFServices.MR_STREAM_VOLUME_SERVICE, typeof(IMFAudioStreamVolume).GUID, out vol);
                if (vol == null)
                    throw new Exception("Could not GetService for MR_POLICY_VOLUME_SERVICE");
                SimpleAudioVolume = vol as IMFAudioStreamVolume;
                if (SimpleAudioVolume == null)
                    throw new Exception("Could not QI for IMFSimpleAudioVolume");
            }
        }
        /// <summary>
        /// Creates a new renderer and configures it with a custom allocator
        /// </summary>
        /// <param name="rendererType">The type of renderer we wish to choose</param>
        /// <param name="graph">The DirectShow graph to add the renderer to</param>
        /// <returns>An initialized DirectShow renderer</returns>
        protected IMFActivate CreateVideoRenderer()
        {
            
            
            //register the allocator and get the events
            //presenter = EvrPresenter.CreateNew();
            //RegisterCustomAllocator(CustomEVR as ICustomAllocator);
            //return hr;
            MediaFoundation.MediaPlayers.EVR.EvrPresenter presenter;
            IMFActivate activate = null;
            
            int hr = 0;
            lock (m_videoRendererInitLock)
            {
                IntPtr handle = GetDesktopWindow();//HwndHelper.Handle;
                
                try
                {
                    hr = MFExtern.MFCreateVideoRendererActivate(handle, out activate);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                //var evr = new EnhancedVideoRenderer();
               

                /* Create a new EVR presenter */
                presenter = MediaFoundation.MediaPlayers.EVR.EvrPresenter.CreateNew(activate);


                var presenterSettings = presenter.VideoPresenter as IEVRPresenterSettings;
                if (presenterSettings == null)
                    throw new Exception("Could not QueryInterface for the IEVRPresenterSettings");

                //presenterSettings.SetBufferCount(3);
                presenterSettings.SetBufferCount(4);
                
                /* Use our interop hWnd */
                

                /* QueryInterface the IMFVideoDisplayControl */
                var displayControl = presenter.VideoPresenter as IMFVideoDisplayControl;

                if (displayControl == null)
                    throw new Exception("Could not QueryInterface the IMFVideoDisplayControl");
                
                /* Configure the presenter with our hWnd */
                hr = displayControl.SetVideoWindow(handle);
                //DsError.ThrowExceptionForHR(hr);

                /*var filterConfig = evr as IEVRFilterConfig;

                if (filterConfig != null)
                    filterConfig.SetNumberOfStreams(1/*streamCount);*/
            }


            RegisterCustomAllocator(presenter as ICustomAllocator);

            return activate;
            
        }

        /// <summary>
        /// Plays the media
        /// </summary>
        public virtual void Play()
        {
            VerifyAccess();

            if (m_pSession != null)
                m_pSession.Start(Guid.Empty,new PropVariant());
        }

        /// <summary>
        /// Stops the media
        /// </summary>
        public virtual void Stop()
        {
            VerifyAccess();

            StopInternal();
        }

        /// <summary>
        /// Stops the media, but does not VerifyAccess() on
        /// the Dispatcher.  This can be used by destructors
        /// because it happens on another thread and our 
        /// DirectShow graph and COM run in MTA
        /// </summary>
        protected void StopInternal()
        {
            if (m_pSession != null)
            {
                m_pSession.Stop();
            }
        }

        /// <summary>
        /// Closes the media and frees its resources
        /// </summary>
        public virtual void Close()
        {
            VerifyAccess();
            StopInternal();
            FreeResources();
        }

        /// <summary>
        /// Pauses the media
        /// </summary>
        public virtual void Pause()
        {
            VerifyAccess();
            
            if (m_pSession != null)
            {
                m_pSession.Pause();
            }
        }

        #region Event Invokes

        /// <summary>
        /// Invokes the MediaEnded event, notifying any subscriber that
        /// media has reached the end
        /// </summary>
        protected void InvokeMediaEnded(EventArgs e)
        {
            var mediaEndedHandler = MediaEnded;
            if (mediaEndedHandler != null)
                mediaEndedHandler();
        }

        /// <summary>
        /// Invokes the MediaOpened event, notifying any subscriber that
        /// media has successfully been opened
        /// </summary>
        protected void InvokeMediaOpened()
        {
            /* This is generally a good place to start
             * our polling timer */
            StartGraphPollTimer();

            var mediaOpenedHandler = MediaOpened;
            if (mediaOpenedHandler != null)
                mediaOpenedHandler();
        }

        /// <summary>
        /// Invokes the MediaClosed event, notifying any subscriber that
        /// the opened media has been closed
        /// </summary>
        protected void InvokeMediaClosed(EventArgs e)
        {
            StopGraphPollTimer();

            var mediaClosedHandler = MediaClosed;
            if (mediaClosedHandler != null)
                mediaClosedHandler();
        }

        /// <summary>
        /// Invokes the MediaFailed event, notifying any subscriber that there was
        /// a media exception.
        /// </summary>
        /// <param name="e">The MediaFailedEventArgs contains the exception that caused this event to fire</param>
        protected void InvokeMediaFailed(MediaFailedEventArgs e)
        {
            var mediaFailedHandler = MediaFailed;
            if (mediaFailedHandler != null)
                mediaFailedHandler(this, e);
        }

        /// <summary>
        /// Invokes the NewAllocatorFrame event, notifying any subscriber that new frame
        /// is ready to be presented.
        /// </summary>
        protected void InvokeNewAllocatorFrame()
        {
            var newAllocatorFrameHandler = NewAllocatorFrame;
            if (newAllocatorFrameHandler != null)
                newAllocatorFrameHandler();
        }

        /// <summary>
        /// Invokes the NewAllocatorSurface event, notifying any subscriber of a new surface
        /// </summary>
        /// <param name="pSurface">The COM pointer to the D3D surface</param>
        protected void InvokeNewAllocatorSurface(IntPtr pSurface)
        {
            var del = NewAllocatorSurface;
            if (del != null)
                del(this, pSurface);
        }

        #endregion

        #region Helper Methods
        /// <summary>
        /// Sets the natural pixel resolution the video in the graph
        /// </summary>
        /// <param name="renderer">The video renderer</param>
        protected void SetNativePixelSizes()
        {
            Size size = GetVideoSize();

            NaturalVideoHeight = (int)size.Height;
            NaturalVideoWidth = (int)size.Width;

            HasVideo = true;
        }

        /// <summary>
        /// Gets the video resolution of a pin on a renderer.
        /// </summary>
        /// <param name="renderer">The renderer to inspect</param>
        /// <param name="direction">The direction the pin is</param>
        /// <param name="pinIndex">The zero based index of the pin to inspect</param>
        /// <returns>If successful a video resolution is returned.  If not, a 0x0 size is returned</returns>
        protected static Size GetVideoSize()
        {
            var size = new Size(0,0);
            //todo
            
            return size;
        }

        #endregion
    }
}