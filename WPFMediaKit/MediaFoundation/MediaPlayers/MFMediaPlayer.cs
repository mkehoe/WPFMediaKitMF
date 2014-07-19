#region Usings
using System;
using System.Runtime.InteropServices;
using MediaFoundation;
using MediaFoundation.Misc;

//using DirectShowLib;
#endregion

namespace MediaFoundation.MediaPlayers
{
    public enum PlayerState
    {
        Ready = 0,
        OpenPending,
        Started,
        PausePending,
        Paused,
        StartPending,
    }
    /// <summary>
    /// The MFMediaUriPlayer plays media files from a given Uri.
    /// </summary>
    public class MFMediaUriPlayer : MFMediaSeekingPlayer
    {
        /// <summary>
        /// The name of the default audio render.  This is the
        /// same on all versions of windows
        /// </summary>
        private const string DEFAULT_AUDIO_RENDERER_NAME = "Default DirectSound Device";

        /// <summary>
        /// Set the default audio renderer property backing
        /// </summary>
        private string m_audioRenderer = DEFAULT_AUDIO_RENDERER_NAME;


        protected PlayerState m_state;
        /// <summary>
        /// The DirectShow graph interface.  In this example
        /// We keep reference to this so we can dispose 
        /// of it later.
        /// </summary>
        

        /// <summary>
        /// The media Uri
        /// </summary>
        private Uri m_sourceUri;

        /// <summary>
        /// Gets or sets the Uri source of the media
        /// </summary>
        public Uri Source
        {
            get
            {
                VerifyAccess();
                return m_sourceUri;
            }
            set
            {
                VerifyAccess();
                m_sourceUri = value;

                OpenSource();
            }
        }

        /// <summary>
        /// The name of the audio renderer device
        /// </summary>
        public string AudioRenderer
        {
            get
            {
                VerifyAccess();
                return m_audioRenderer;
            }
            set
            {
                VerifyAccess();

                if (string.IsNullOrEmpty(value))
                {
                    value = DEFAULT_AUDIO_RENDERER_NAME;
                }

                m_audioRenderer = value;
            }
        }

        /// <summary>
        /// Gets or sets if the media should play in loop
        /// or if it should just stop when the media is complete
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// Is ran everytime a new media event occurs on the graph
        /// </summary>
        /// <param name="code">The Event code that occured</param>
        /// <param name="lparam1">The first event parameter sent by the graph</param>
        /// <param name="lparam2">The second event parameter sent by the graph</param>
        protected override void OnMediaEvent(IMFMediaEvent code)
        {
            MediaEventType MedEventType;
            code.GetType(out MedEventType);
            if (Loop && MedEventType == MediaEventType.MESessionEnded)
            {
                MediaPosition = 0;
            }
            else
                /* Only run the base when we don't loop
                 * otherwise the default behavior is to
                 * fire a media ended event */
                base.OnMediaEvent(code);

            //COMBase.SafeRelease(MedEventType);
        }
        
        
        
        
        protected void CreateMediaSource(string sURL)
        {
            

            IMFSourceResolver pSourceResolver;
            object pSource;

            // Create the source resolver.
            int hr = MFExtern.MFCreateSourceResolver(out pSourceResolver);
            MFError.ThrowExceptionForHR(hr);

            try
            {
                // Use the source resolver to create the media source.
                MFObjectType ObjectType = MFObjectType.Invalid;

                hr = pSourceResolver.CreateObjectFromURL(
                        sURL,                       // URL of the source.
                        MFResolution.MediaSource,   // Create a source object.
                        null,                       // Optional property store.
                        out ObjectType,             // Receives the created object type.
                        out pSource                 // Receives a pointer to the media source.
                    );
                MFError.ThrowExceptionForHR(hr);

                // Get the IMFMediaSource interface from the media source.
                m_pSource = pSource as IMFMediaSource;
            }
            finally
            {
                // Clean up
                Marshal.ReleaseComObject(pSourceResolver);
            }
        }
        
       
        /// <summary>
        /// Opens the media by initializing the DirectShow graph
        /// </summary>
        protected virtual void OpenSource()
        {
            /* Make sure we clean up any remaining mess */
            FreeResources();
            string sURL = m_sourceUri.ToString();

            if (string.IsNullOrEmpty(sURL))
                return;
            int hr = 0;
            try
            {
                IMFTopology pTopology = null;

                // Create the media session.
                CreateSession();

                // Create the media source.
                CreateMediaSource(sURL);
                
                // Create a partial topology.
                pTopology = CreateTopologyFromSource();

                // Set the topology on the media session.
                hr = m_pSession.SetTopology(0, pTopology);
                MFError.ThrowExceptionForHR(hr);

                // Set our state to "open pending"
                m_state = PlayerState.OpenPending;
                //NotifyState();

                m_pSession.GetClock(out pClock);
                
                COMBase.SafeRelease(pTopology);

                SetDuration();
                //AddAudioInterface();

                // If SetTopology succeeded, the media session will queue an
                // MESessionTopologySet event.
            }
            catch (Exception ce)
            {
                hr = Marshal.GetHRForException(ce);
                /* This exection will happen usually if the media does
                 * not exist or could not open due to not having the
                 * proper filters installed */
                FreeResources();

                /* Fire our failed event */
                InvokeMediaFailed(new MediaFailedEventArgs(ce.Message, ce));
                
            }

            

            InvokeMediaOpened();
        }

        /// <summary>
        /// Frees all unmanaged memory and resets the object back
        /// to its initial state
        /// </summary>
        protected override void FreeResources()
        {


            /* We run the StopInternal() to avoid any 
             * Dispatcher VeryifyAccess() issues because
             * this may be called from the GC */
            StopInternal();

            /* Let's clean up the base 
             * class's stuff first */
            base.FreeResources();

            /*if (m_graph != null)
            {
                Marshal.ReleaseComObject(m_graph);
                m_graph = null;
            */
                /* Only run the media closed if we have an
                 * initialized filter graph */
                InvokeMediaClosed(new EventArgs());
            //}
        }
    }
}