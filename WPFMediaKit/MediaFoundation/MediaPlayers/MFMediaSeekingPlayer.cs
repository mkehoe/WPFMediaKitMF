using System;
using System.Runtime.InteropServices;
using MediaFoundation.Misc;
//using DirectShowLib;

namespace MediaFoundation.MediaPlayers
{
    /// <summary>
    /// The MFMediaSeekingPlayer adds media seeking functionality to
    /// to the MediaPlayerBase class
    /// </summary>
    public abstract class MFMediaSeekingPlayer : MFMediaPlayerBase
    {

        /// <summary>
        /// Local cache of the current position
        /// </summary>
        private long m_currentPosition;

        private long m_nextSeekTime = -1;
        private long cachedSeek = 0;
        protected bool bIsSeeking = false;
        protected bool bIsScrubbing = false;
        /// <summary>
        /// Gets the duration in miliseconds, of the media that is opened
        /// </summary>
        public virtual long Duration { get; protected set; }
        protected IMFClock pClock;

        private IMFRateControl RateControl = null;

        private void GetRateControl()
        {
            if (RateControl != null)
                return;
            if (m_pSession == null)
                return;
            object RC = null;
            MFExtern.MFGetService(m_pSession, MFServices.MF_RATE_CONTROL_SERVICE, typeof(IMFRateControl).GUID, out RC);
            
            RateControl = RC as IMFRateControl;
            if (RateControl == null)
                throw new Exception("Couldnt get the rate control interface");
        }
        /// <summary>
        /// Sets the rate at which the media plays back
        /// </summary>
        public double SpeedRatio
        {
            get
            {
                if (m_pSession == null)
                    return 1.0;
                GetRateControl();
                
                float rate;
                bool thin = false;
                int hr = RateControl.GetRate(ref thin, out rate);
                return rate;
            }
            set
            {
                if (m_pSession == null)
                    return;
                GetRateControl();
                
                int hr = RateControl.SetRate(false, (float)value); 
                
                if(hr != 0)
                  throw new Exception ("Failed to Set rate");
            }
        }

        /// <summary>
        /// Gets or sets the position in miliseconds of the media
        /// </summary>
        public virtual long MediaPosition
        {
            get
            {
                VerifyAccess();
                if (m_nextSeekTime > 0)
                {
                    return m_nextSeekTime;
                }
                else if (bIsSeeking)
                {
                    return cachedSeek;
                }
                else
                {
                    return m_currentPosition;
                }
            }
            set
            {
                VerifyAccess();
                cachedSeek = value;
                PropVariant pv = new PropVariant(cachedSeek);
                if (bIsSeeking)
                {
                    m_nextSeekTime = cachedSeek;
                    return;
                }     
                if (m_pSession != null)
                {
                    //this.SpeedRatio = 0.0;
                    m_pSession.Start(Guid.Empty, pv);
                    bIsSeeking = true;
                }
            
            }
        }

        /// <summary>
        /// The current position format the media is using
        /// </summary>
        private MediaPositionFormat m_currentPositionFormat;

        /// <summary>
        /// The prefered position format to use with the media
        /// </summary>
        private MediaPositionFormat m_preferedPositionFormat;

        /// <summary>
        /// The current media positioning format
        /// </summary>
        public virtual MediaPositionFormat CurrentPositionFormat
        {
            get { return m_currentPositionFormat; }
            protected set { m_currentPositionFormat = value; }
        }

        /// <summary>
        /// The prefered media positioning format
        /// </summary>
        public virtual MediaPositionFormat PreferedPositionFormat
        {
            get { return m_preferedPositionFormat; }
            set
            {
                m_preferedPositionFormat = value;
                
                //SetMediaSeekingInterface(m_mediaSeeking);
            }
        }

        /// <summary>
        /// Notifies when the position of the media has changed
        /// </summary>
        public event EventHandler MediaPositionChanged;

        protected void InvokeMediaPositionChanged(EventArgs e)
        {
            EventHandler mediaPositionChangedHandler = MediaPositionChanged;
            if (mediaPositionChangedHandler != null) mediaPositionChangedHandler(this, e);
        }

        /// <summary>
        /// Frees any allocated or unmanaged resources
        /// </summary>
        protected override void FreeResources()
        {
            base.FreeResources();

            /*if (m_mediaSeeking != null)
                Marshal.ReleaseComObject(m_mediaSeeking);

            m_mediaSeeking = null;*/
            m_currentPosition = 0;
        }

        /// <summary>
        /// Polls the graph for various data about the media that is playing
        /// </summary>
        protected override void OnTimerTick()
        {
            /* Polls the current position */
            if (pClock != null)
            {
                long sysTime = 0;
                long cTime = 0;
                pClock.GetCorrelatedTime(0, out cTime, out sysTime);
                if (cTime != m_currentPosition)
                {
                    m_currentPosition = cTime;
                    InvokeMediaPositionChanged(null);
                }
            }

            base.OnTimerTick();
        }


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
            if (MedEventType == MediaEventType.MESessionStarted /*|| MedEventType == MediaEventType.MESessionScrubSampleComplete*/)
            {
               if (bIsSeeking)
                {
                    bIsSeeking = false;
                    if (m_nextSeekTime > 0)
                    {
                        this.MediaPosition = m_nextSeekTime;
                        m_nextSeekTime = -1;
                    }   
                    //this.SpeedRatio = 1.0;
                }
            }
            base.OnMediaEvent(code);

            
        }
        

        protected void SetDuration()
        {
            if (m_pSource == null)
                return;


            long duration = 0;
            IMFPresentationDescriptor pPD = null;
            int hr = m_pSource.CreatePresentationDescriptor(out pPD);
            if (hr == 0)
            {
                pPD.GetUINT64(MFAttributesClsid.MF_PD_DURATION, out duration);
            }
            COMBase.SafeRelease(pPD);
            Duration = duration ;
            
        }

        /// <summary>
        /// Setup the IMediaSeeking interface
        /// </summary>
        
    }
}
