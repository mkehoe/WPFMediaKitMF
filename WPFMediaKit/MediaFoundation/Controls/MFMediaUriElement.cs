using System;
using System.Windows;
using System.Windows.Threading;
using MediaFoundation.MediaPlayers;

namespace MediaFoundation.Controls
{
    /// <summary>
    /// The MediaUriElement is a WPF control that plays media of a given
    /// Uri. The Uri can be a file path or a Url to media.  The MediaUriElement
    /// inherits from the MediaSeekingElement, so where available, seeking is
    /// also supported.
    /// </summary>
    public class MFMediaUriElement : MFMediaSeekingElement
    {
        /// <summary>
        /// The current MediaUriPlayer
        /// </summary>
        protected MFMediaUriPlayer MFMediaUriPlayer
        {
            get
            {
                return MFMediaPlayerBase as MFMediaUriPlayer;
            }
        }

        #region VideoRenderer

        /*public static readonly DependencyProperty VideoRendererProperty =
            DependencyProperty.Register("VideoRenderer", typeof(VideoRendererType), typeof(MediaUriElement),
                new FrameworkPropertyMetadata(VideoRendererType.VideoMixingRenderer9,
                    new PropertyChangedCallback(OnVideoRendererChanged)));*/

        /*public VideoRendererType VideoRenderer
        {
            get { return (VideoRendererType)GetValue(VideoRendererProperty); }
            set { SetValue(VideoRendererProperty, value); }
        }*/

        private static void OnVideoRendererChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((MFMediaUriElement)d).OnVideoRendererChanged(e);
        }

        protected virtual void OnVideoRendererChanged(DependencyPropertyChangedEventArgs e)
        {
            //if (HasInitialized)
                //PlayerSetVideoRenderer();
        }

        /*private void PlayerSetVideoRenderer()
        {
            var videoRendererType = VideoRenderer;
            MediaUriPlayer.Dispatcher.BeginInvoke((Action)delegate
            {
                MediaUriPlayer.VideoRenderer = videoRendererType;
            });
        }*/

        #endregion

        #region AudioRenderer

        public static readonly DependencyProperty AudioRendererProperty =
            DependencyProperty.Register("AudioRenderer", typeof(string), typeof(MFMediaUriElement),
                new FrameworkPropertyMetadata("Default DirectSound Device",
                    new PropertyChangedCallback(OnAudioRendererChanged)));

        /// <summary>
        /// The name of the audio renderer device to use
        /// </summary>
        public string AudioRenderer
        {
            get { return (string)GetValue(AudioRendererProperty); }
            set { SetValue(AudioRendererProperty, value); }
        }

        private static void OnAudioRendererChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((MFMediaUriElement)d).OnAudioRendererChanged(e);
        }

        protected virtual void OnAudioRendererChanged(DependencyPropertyChangedEventArgs e)
        {
            if (HasInitialized)
                PlayerSetAudioRenderer();
        }

        private void PlayerSetAudioRenderer()
        {
            var audioDevice = AudioRenderer;

            MFMediaUriPlayer.Dispatcher.BeginInvoke((Action)delegate
            {
                /* Sets the audio device to use with the player */
                MFMediaUriPlayer.AudioRenderer = audioDevice;
            });
        }

        #endregion

        #region Source

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(Uri), typeof(MFMediaUriElement),
                new FrameworkPropertyMetadata(null,
                    new PropertyChangedCallback(OnSourceChanged)));

        /// <summary>
        /// The Uri source to the media.  This can be a file path or a
        /// URL source
        /// </summary>
        public Uri Source
        {
            get { return (Uri)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((MFMediaUriElement)d).OnSourceChanged(e);
        }

        protected void OnSourceChanged(DependencyPropertyChangedEventArgs e)
        {
            if (HasInitialized)
                PlayerSetSource();
        }

        private void PlayerSetSource()
        {
            var source = Source;
            //var rendererType = VideoRenderer;

            MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)delegate
            {
                /* Set the renderer type */
               // MFMediaUriPlayer.VideoRenderer = rendererType;

                /* Set the source type */
                MFMediaUriPlayer.Source = source;

                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (IsLoaded)
                        ExecuteMediaState(LoadedBehavior);
                    //else
                    //    ExecuteMediaState(UnloadedBehavior);
                });
            });
        }
        #endregion

        #region Loop

        public static readonly DependencyProperty LoopProperty =
            DependencyProperty.Register("Loop", typeof(bool), typeof(MFMediaUriElement),
                new FrameworkPropertyMetadata(false,
                    new PropertyChangedCallback(OnLoopChanged)));

        /// <summary>
        /// Gets or sets whether the media should return to the begining
        /// once the end has reached
        /// </summary>
        public bool Loop
        {
            get { return (bool)GetValue(LoopProperty); }
            set { SetValue(LoopProperty, value); }
        }

        private static void OnLoopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((MFMediaUriElement)d).OnLoopChanged(e);
        }

        protected virtual void OnLoopChanged(DependencyPropertyChangedEventArgs e)
        {
            if (HasInitialized)
                PlayerSetLoop();
        }

        private void PlayerSetLoop()
        {
            var loop = Loop;
            MFMediaPlayerBase.Dispatcher.BeginInvoke((Action)delegate
            {
                MFMediaUriPlayer.Loop = loop;
            });
        }
        #endregion

        public override void EndInit()
        {
            //PlayerSetVideoRenderer();
            PlayerSetAudioRenderer();
            PlayerSetLoop();
            PlayerSetSource();
            base.EndInit();
        }

        /// <summary>
        /// The Play method is overrided so we can
        /// set the source to the media
        /// </summary>
        public override void Play()
        {
            EnsurePlayerThread();
            base.Play();
        }

        /// <summary>
        /// The Pause method is overrided so we can
        /// set the source to the media
        /// </summary>
        public override void Pause()
        {
            EnsurePlayerThread();

            base.Pause();
        }

        /// <summary>
        /// Gets the instance of the media player to initialize
        /// our base classes with
        /// </summary>
        protected override MFMediaPlayerBase OnRequestMediaPlayer()
        {
            var player = new MFMediaUriPlayer();
            return player;
        }
    }
}
