using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
//using WPFMediaKit.DirectShow.MediaPlayers;
using MediaFoundation.MediaPlayers;

namespace MediaFoundation.Controls
{
    /// <summary>
    /// The MediaElementBase is the base WPF control for
    /// making custom media players.  The MediaElement uses the
    /// D3DRenderer class for rendering video
    /// </summary>
    public abstract class MFMediaElementBase : WPFMediaKit.DirectShow.Controls.D3DRenderer
    {
        private Window m_currentWindow;
        private bool m_windowHooked;

        ~MFMediaElementBase()
        {

        }

        #region Routed Events
        #region MediaOpened

        public static readonly RoutedEvent MediaOpenedEvent = EventManager.RegisterRoutedEvent("MediaOpened",
                                                                                               RoutingStrategy.Bubble,
                                                                                               typeof(RoutedEventHandler
                                                                                                   ),
                                                                                               typeof(MFMediaElementBase));

        /// <summary>
        /// Fires when media has successfully been opened
        /// </summary>
        public event RoutedEventHandler MediaOpened
        {
            add { AddHandler(MediaOpenedEvent, value); }
            remove { RemoveHandler(MediaOpenedEvent, value); }
        }

        #endregion

        #region MediaClosed

        public static readonly RoutedEvent MediaClosedEvent = EventManager.RegisterRoutedEvent("MediaClosed",
                                                                                               RoutingStrategy.Bubble,
                                                                                               typeof(RoutedEventHandler),
                                                                                               typeof(MFMediaElementBase));

        /// <summary>
        /// Fires when media has been closed
        /// </summary>
        public event RoutedEventHandler MediaClosed
        {
            add { AddHandler(MediaClosedEvent, value); }
            remove { RemoveHandler(MediaClosedEvent, value); }
        }

        #endregion

        #region MediaEnded

        public static readonly RoutedEvent MediaEndedEvent = EventManager.RegisterRoutedEvent("MediaEnded",
                                                                                              RoutingStrategy.Bubble,
                                                                                              typeof(RoutedEventHandler),
                                                                                              typeof(MFMediaElementBase));

        /// <summary>
        /// Fires when media has completed playing
        /// </summary>
        public event RoutedEventHandler MediaEnded
        {
            add { AddHandler(MediaEndedEvent, value); }
            remove { RemoveHandler(MediaEndedEvent, value); }
        }

        #endregion
        #endregion

        #region Dependency Properties
        #region UnloadedBehavior

        public static readonly DependencyProperty UnloadedBehaviorProperty =
            DependencyProperty.Register("UnloadedBehavior", typeof(MediaFoundation.MediaPlayers.MediaState), typeof(MFMediaElementBase),
                                        new FrameworkPropertyMetadata(MediaFoundation.MediaPlayers.MediaState.Close));

        /// <summary>
        /// Defines the behavior of the control when it is unloaded
        /// </summary>
        public MediaFoundation.MediaPlayers.MediaState UnloadedBehavior
        {
            get { return (MediaFoundation.MediaPlayers.MediaState)GetValue(UnloadedBehaviorProperty); }
            set { SetValue(UnloadedBehaviorProperty, value); }
        }

        #endregion

        #region LoadedBehavior

        public static readonly DependencyProperty LoadedBehaviorProperty =
            DependencyProperty.Register("LoadedBehavior", typeof(MediaFoundation.MediaPlayers.MediaState), typeof(MFMediaElementBase),
                                        new FrameworkPropertyMetadata(MediaFoundation.MediaPlayers.MediaState.Play));

        /// <summary>
        /// Defines the behavior of the control when it is loaded
        /// </summary>
        public MediaFoundation.MediaPlayers.MediaState LoadedBehavior
        {
            get { return (MediaFoundation.MediaPlayers.MediaState)GetValue(LoadedBehaviorProperty); }
            set { SetValue(LoadedBehaviorProperty, value); }
        }

        #endregion

        #region Volume

        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register("Volume", typeof(double), typeof(MFMediaElementBase),
                new FrameworkPropertyMetadata(1.0d,
                    new PropertyChangedCallback(OnVolumeChanged)));

        /// <summary>
        /// Gets or sets the audio volume.  Specifies the volume, as a 
        /// number from 0 to 1.  Full volume is 1, and 0 is silence.
        /// </summary>
        public double Volume
        {
            get { return (double)GetValue(VolumeProperty); }
            set { SetValue(VolumeProperty, value); }
        }

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((MFMediaElementBase)d).OnVolumeChanged(e);
        }

        protected virtual void OnVolumeChanged(DependencyPropertyChangedEventArgs e)
        {
            if (HasInitialized)
                MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)delegate
                {
                    MFMediaPlayerBase.Volume = (double)e.NewValue;
                });
        }

        #endregion

        #region IsPlaying

        private static readonly DependencyPropertyKey IsPlayingPropertyKey
            = DependencyProperty.RegisterReadOnly("IsPlaying", typeof(bool), typeof(MFMediaElementBase),
                new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty IsPlayingProperty
            = IsPlayingPropertyKey.DependencyProperty;

        public bool IsPlaying
        {
            get { return (bool)GetValue(IsPlayingProperty); }
        }

        protected void SetIsPlaying(bool value)
        {
            SetValue(IsPlayingPropertyKey, value);
        }
        #endregion

        #endregion

        #region Commands
        public static readonly RoutedCommand PlayerStateCommand = new RoutedCommand();
        public static readonly RoutedCommand TogglePlayPauseCommand = new RoutedCommand();

        protected virtual void OnPlayerStateCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is MediaFoundation.MediaPlayers.MediaState == false)
                return;

            var state = (MediaFoundation.MediaPlayers.MediaState)e.Parameter;

            ExecuteMediaState(state);
        }

        protected virtual void OnCanExecutePlayerStateCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        protected virtual void OnTogglePlayPauseCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        protected virtual void OnCanExecuteTogglePlayPauseCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
        #endregion

        /// <summary>
        /// Notifies when the media has failed and produced an exception
        /// </summary>
        public event EventHandler<MediaFailedEventArgs> MediaFailed;

        protected MFMediaElementBase()
        {
            DefaultApartmentState = ApartmentState.MTA;

            InitializeMediaPlayerPrivate();
            Loaded += MediaElementBaseLoaded;
            Unloaded += MediaElementBaseUnloaded;



            CommandBindings.Add(new CommandBinding(PlayerStateCommand,
                                                   OnPlayerStateCommandExecuted,
                                                   OnCanExecutePlayerStateCommand));

            CommandBindings.Add(new CommandBinding(TogglePlayPauseCommand,
                                                   OnTogglePlayPauseCommandExecuted,
                                                   OnCanExecuteTogglePlayPauseCommand));
        }

        private void InitializeMediaPlayerPrivate()
        {
            InitializeMediaPlayer();
        }

        protected MFMediaPlayerBase MFMediaPlayerBase { get; set; }

        protected ApartmentState DefaultApartmentState { get; set; }

        protected void EnsurePlayerThread()
        {
            MFMediaPlayerBase.EnsureThread(DefaultApartmentState);
        }

        /// <summary>
        /// Initializes the media player, hooking into events
        /// and other general setup.
        /// </summary>
        protected virtual void InitializeMediaPlayer()
        {
            if (MFMediaPlayerBase != null)
                return;

            MFMediaPlayerBase = OnRequestMediaPlayer();
            EnsurePlayerThread();

            if (MFMediaPlayerBase == null)
            {
                throw new Exception("OnRequestMediaPlayer cannot return null");
            }

            /* Hook into the normal .NET events */
            MFMediaPlayerBase.MediaOpened += OnMediaPlayerOpenedPrivate;
            MFMediaPlayerBase.MediaClosed += OnMediaPlayerClosedPrivate;
            MFMediaPlayerBase.MediaFailed += OnMediaPlayerFailedPrivate;
            MFMediaPlayerBase.MediaEnded += OnMediaPlayerEndedPrivate;

            /* These events fire when we get new D3Dsurfaces or frames */
            MFMediaPlayerBase.NewAllocatorFrame += OnMediaPlayerNewAllocatorFramePrivate;
            MFMediaPlayerBase.NewAllocatorSurface += OnMediaPlayerNewAllocatorSurfacePrivate;
        }

        #region Private Event Handlers
        private void OnMediaPlayerFailedPrivate(object sender, MediaFailedEventArgs e)
        {
            OnMediaPlayerFailed(e);
        }

        private void OnMediaPlayerNewAllocatorSurfacePrivate(object sender, IntPtr pSurface)
        {
            OnMediaPlayerNewAllocatorSurface(pSurface);
        }

        private void OnMediaPlayerNewAllocatorFramePrivate()
        {
            OnMediaPlayerNewAllocatorFrame();
        }

        private void OnMediaPlayerClosedPrivate()
        {
            OnMediaPlayerClosed();
        }

        private void OnMediaPlayerEndedPrivate()
        {
            OnMediaPlayerEnded();
        }

        private void OnMediaPlayerOpenedPrivate()
        {
            OnMediaPlayerOpened();
        }
        #endregion

        /// <summary>
        /// Fires the MediaFailed event
        /// </summary>
        /// <param name="e">The failed media arguments</param>
        protected void InvokeMediaFailed(MediaFailedEventArgs e)
        {
            EventHandler<MediaFailedEventArgs> mediaFailedHandler = MediaFailed;
            if (mediaFailedHandler != null) mediaFailedHandler(this, e);
        }

        /// <summary>
        /// Executes when a media operation failed
        /// </summary>
        /// <param name="e">The failed event arguments</param>
        protected virtual void OnMediaPlayerFailed(MediaFailedEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() => SetIsPlaying(false)));
            InvokeMediaFailed(e);
        }

        /// <summary>
        /// Is executes when a new D3D surfaces has been allocated
        /// </summary>
        /// <param name="pSurface">The pointer to the D3D surface</param>
        protected virtual void OnMediaPlayerNewAllocatorSurface(IntPtr pSurface)
        {
            SetBackBuffer(pSurface);
        }

        /// <summary>
        /// Called for every frame in media that has video
        /// </summary>
        protected virtual void OnMediaPlayerNewAllocatorFrame()
        {
            InvalidateVideoImage();
        }

        /// <summary>
        /// Called when the media has been closed
        /// </summary>
        protected virtual void OnMediaPlayerClosed()
        {
            Dispatcher.BeginInvoke((Action)(() => SetIsPlaying(false)));
            Dispatcher.BeginInvoke((Action)(() => RaiseEvent(new RoutedEventArgs(MediaClosedEvent))));
        }

        /// <summary>
        /// Called when the media has ended
        /// </summary>
        protected virtual void OnMediaPlayerEnded()
        {
            Dispatcher.BeginInvoke((Action)(() => SetIsPlaying(false)));
            Dispatcher.BeginInvoke((Action)(() => RaiseEvent(new RoutedEventArgs(MediaEndedEvent))));
        }

        /// <summary>
        /// Executed when media has successfully been opened.
        /// </summary>
        protected virtual void OnMediaPlayerOpened()
        {
            /* Safely grab out our values */
            bool hasVideo = MFMediaPlayerBase.HasVideo;
            int videoWidth = MFMediaPlayerBase.NaturalVideoWidth;
            int videoHeight = MFMediaPlayerBase.NaturalVideoHeight;
            double volume;
            //double balance;

            Dispatcher.BeginInvoke((Action)delegate
            {
                /* If we have no video just black out the video
                 * area by releasing the D3D surface */
                if (!hasVideo)
                {
                    SetBackBuffer(IntPtr.Zero);
                }

                SetNaturalVideoWidth(videoWidth);
                SetNaturalVideoHeight(videoHeight);

                /* Set our dp values to match the media player */
                SetHasVideo(hasVideo);

                /* Get our DP values */
                volume = Volume;
                //balance = Balance;

                /* Make sure our volume and balances are set */
                MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)delegate
                {
                    MFMediaPlayerBase.Volume = volume;
                   // MFMediaPlayerBase.Balance = balance;
                });
                SetIsPlaying(true);
                RaiseEvent(new RoutedEventArgs(MediaOpenedEvent));
            });
        }

        /// <summary>
        /// Fires when the owner window is closed.  Nothing will happen
        /// if the visual does not belong to the visual tree with a root
        /// of a WPF window
        /// </summary>
        private void WindowOwnerClosed(object sender, EventArgs e)
        {
            ExecuteMediaState(UnloadedBehavior);
        }

        /// <summary>
        /// Local handler for the Loaded event
        /// </summary>
        private void MediaElementBaseUnloaded(object sender, RoutedEventArgs e)
        {
            /* Make sure we call our virtual method every time! */
            OnUnloadedOverride();

            if (Application.Current == null)
                return;

            m_windowHooked = false;

            if (m_currentWindow == null)
                return;

            m_currentWindow.Closed -= WindowOwnerClosed;
            m_currentWindow = null;
        }

        /// <summary>
        /// Local handler for the Unloaded event
        /// </summary>
        private void MediaElementBaseLoaded(object sender, RoutedEventArgs e)
        {
            m_currentWindow = Window.GetWindow(this);

            if (m_currentWindow != null && !m_windowHooked)
            {
                m_currentWindow.Closed += WindowOwnerClosed;
                m_windowHooked = true;
            }

            OnLoadedOverride();
        }

        /// <summary>
        /// Runs when the Loaded event is fired and executes
        /// the LoadedBehavior
        /// </summary>
        protected virtual void OnLoadedOverride()
        {
            ExecuteMediaState(LoadedBehavior);
        }

        /// <summary>
        /// Runs when the Unloaded event is fired and executes
        /// the UnloadedBehavior
        /// </summary>
        protected virtual void OnUnloadedOverride()
        {
            ExecuteMediaState(UnloadedBehavior);
        }

        /// <summary>
        /// Executes the actions associated to a MediaState
        /// </summary>
        /// <param name="state">The MediaState to execute</param>
        protected void ExecuteMediaState(MediaFoundation.MediaPlayers.MediaState state)
        {
            switch (state)
            {
                case MediaFoundation.MediaPlayers.MediaState.Manual:
                    break;
                case MediaFoundation.MediaPlayers.MediaState.Play:
                    Play();
                    break;
                case MediaFoundation.MediaPlayers.MediaState.Stop:
                    Stop();
                    break;
                case MediaFoundation.MediaPlayers.MediaState.Close:
                    Close();
                    break;
                case MediaFoundation.MediaPlayers.MediaState.Pause:
                    Pause();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override void BeginInit()
        {
            HasInitialized = false;
            base.BeginInit();
        }

        public override void EndInit()
        {
            //double balance = Balance;
            double volume = Volume;

            MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)delegate
            {
                //MFMediaPlayerBase.Balance = balance;
                MFMediaPlayerBase.Volume = volume;
            });

            HasInitialized = true;
            base.EndInit();
        }

        public bool HasInitialized
        {
            get;
            protected set;
        }

        /// <summary>
        /// Plays the media
        /// </summary>
        public virtual void Play()
        {
            MFMediaPlayerBase.EnsureThread(DefaultApartmentState);
            MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)(delegate
            {
                MFMediaPlayerBase.Play();
                Dispatcher.BeginInvoke(((Action)(() => SetIsPlaying(true))));
            }));

        }

        /// <summary>
        /// Pauses the media
        /// </summary>
        public virtual void Pause()
        {
            MFMediaPlayerBase.EnsureThread(DefaultApartmentState);
            MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)(() => MFMediaPlayerBase.Pause()));
            SetIsPlaying(false);
        }

        /// <summary>
        /// Closes the media
        /// </summary>
        public virtual void Close()
        {
            SetBackBuffer(IntPtr.Zero);
            InvalidateVideoImage();

            if (!MFMediaPlayerBase.Dispatcher.Shutdown || !MFMediaPlayerBase.Dispatcher.ShuttingDown)
                MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)(delegate
                {
                    MFMediaPlayerBase.Close();
                    MFMediaPlayerBase.Dispose();
                }));

            SetIsPlaying(false);
        }

        /// <summary>
        /// Stops the media
        /// </summary>
        public virtual void Stop()
        {
            if (!MFMediaPlayerBase.Dispatcher.Shutdown || !MFMediaPlayerBase.Dispatcher.ShuttingDown)
                MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)(() => MFMediaPlayerBase.Stop()));

            SetIsPlaying(false);
        }

        /// <summary>
        /// Called when a MediaPlayerBase is required.
        /// </summary>
        /// <returns>This method must return a valid (not null) MediaPlayerBase</returns>
        protected virtual MFMediaPlayerBase OnRequestMediaPlayer()
        {
            return null;
        }
    }
}