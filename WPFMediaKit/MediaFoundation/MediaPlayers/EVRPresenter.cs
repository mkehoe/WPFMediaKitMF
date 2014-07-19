#region Usings
using System;
using System.Runtime.InteropServices;
using System.Security;
using WPFMediaKit.DirectShow.MediaPlayers;
using MediaFoundation.EVR;
using MediaFoundation.MediaPlayers;

#endregion

namespace MediaFoundation.MediaPlayers.EVR
{
    #region Custom COM Types

    [ComVisible(true), ComImport, SuppressUnmanagedCodeSecurity,
     Guid("00000001-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance([In, MarshalAs(UnmanagedType.Interface)] object pUnkOuter,
                           ref Guid riid,
                           [Out, MarshalAs(UnmanagedType.Interface)] out object obj);

        [PreserveSig]
        int LockServer([In] bool fLock);
    }

    [ComVisible(true), ComImport, SuppressUnmanagedCodeSecurity,
     Guid("B92D8991-6C42-4e51-B942-E61CB8696FCB"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEVRPresenterCallback
    {
        [PreserveSig]
        int PresentSurfaceCB(IntPtr pSurface);
    }

    [ComVisible(true), ComImport, SuppressUnmanagedCodeSecurity,
     Guid("9019EA9C-F1B4-44b5-ADD5-D25704313E48"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEVRPresenterRegisterCallback
    {
        [PreserveSig]
        int RegisterCallback(IEVRPresenterCallback pCallback);
    }

    [ComVisible(true), ComImport, SuppressUnmanagedCodeSecurity,
     Guid("4527B2E7-49BE-4b61-A19D-429066D93A99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEVRPresenterSettings
    {
        [PreserveSig]
        int SetBufferCount(int bufferCount);
    }
    #endregion

    [ComVisible(true)]
    public class EvrPresenter : ICustomAllocator, IEVRPresenterCallback
    {
        private IntPtr m_lastSurface;

        private EvrPresenter()
        {
        }

        #region Interop

        /// <summary>
        /// The GUID of our EVR custom presenter COM object
        /// </summary>
        private static readonly Guid EVR_PRESENTER_CLSID = new Guid(0x9807fc9c, 0x807b, 0x41e3, 0x98, 0xa8, 0x75, 0x17,
                                                                    0x6f, 0x95, 0xa0, 0x63);

        /// <summary>
        /// The GUID of IUnknown
        /// </summary>
        private static readonly Guid IUNKNOWN_GUID = new Guid("{00000000-0000-0000-C000-000000000046}");

        /// <summary>
        /// Static method in the 32 bit dll to create our IClassFactory
        /// </summary>
        [PreserveSig]
        [DllImport("EvrPresenterx86.dll", EntryPoint = "DllGetClassObject")]
        private static extern int DllGetClassObject32([MarshalAs(UnmanagedType.LPStruct)] Guid clsid,
                                                      [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                                                      [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        /// <summary>
        /// Static method in the 62 bit dll to create our IClassFactory
        /// </summary>
        [PreserveSig]
        [DllImport("EvrPresenterx64.dll", EntryPoint = "DllGetClassObject")]
        private static extern int DllGetClassObject64([MarshalAs(UnmanagedType.LPStruct)] Guid clsid,
                                                      [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                                                      [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        #endregion

        /// <summary>
        /// Returns the bittage of this process, ie 32 or 64 bit
        /// </summary>
        private static int ProcessBits
        {
            get { return IntPtr.Size*8; }
        }

        /// <summary>
        /// The custom EVR video presenter COM object
        /// </summary>
        public IMFVideoPresenter VideoPresenter { get; private set; }

        #region ICustomAllocator Members
        /// <summary>
        /// Invokes when a new frame has been allocated
        /// to a surface
        /// </summary>
        public event Action NewAllocatorFrame;

        /// <summary>
        /// Invokes when a new surface has been allocated
        /// </summary>
        public event NewAllocatorSurfaceDelegate NewAllocatorSurface;

        public void Dispose()
        {
            Marshal.ReleaseComObject(VideoPresenter);
        }

        #endregion

        #region IEVRPresenterCallback Members

        /// <summary>
        /// Called by the custom EVR Presenter, notifying that
        /// there is a new D3D surface and/or there needs to be
        /// a frame rendered
        /// </summary>
        /// <param name="pSurface">The Direct3D surface</param>
        /// <returns>A HRESULT</returns>
        public int PresentSurfaceCB(IntPtr pSurface)
        {
            /* Check if the surface is the same as the last*/
            if(m_lastSurface != pSurface)
                InvokeNewAllocatorSurface(pSurface);

            /* Store ref to the pointer so we can compare
             * it next time this method is called */
            m_lastSurface = pSurface;

            InvokeNewAllocatorFrame();
            return 0;
        }

        #endregion

        /// <summary>
        /// Create a new EVR video presenter
        /// </summary>
        /// <returns></returns>
        public static EvrPresenter CreateNew(IMFActivate activate)
        {
            object comObject = null;

            int hr = 0;

            /* Our exception var we use to hold the exception
             * until we need to throw it (after clean up) */
            Exception exception = null;

            /* A COM object we query form our native library */
            IClassFactory factory = null;

            /* Create our 'helper' class */
            var evrPresenter = new EvrPresenter();
            
            /* Call the DLL export to create the class factory */
            if (ProcessBits == 32)
            {
                //activate.SetGUID(MFAttributesClsid.MF_ACTIVATE_CUSTOM_VIDEO_PRESENTER_CLSID, EVR_PRESENTER_CLSID);
                hr = DllGetClassObject32(EVR_PRESENTER_CLSID, IUNKNOWN_GUID, out comObject);
                //activate.SetUnknown(MFAttributesClsid.MF_ACTIVATE_CUSTOM_VIDEO_PRESENTER_ACTIVATE, evrPresenter as IMFActivate);
                //activate.ActivateObject(EVR_PRESENTER_CLSID, out comObject);
               
            }
            else if (ProcessBits == 64)
            {
                activate.SetGUID(MFAttributesClsid.MF_ACTIVATE_CUSTOM_VIDEO_PRESENTER_CLSID, EVR_PRESENTER_CLSID);
                hr = DllGetClassObject64(EVR_PRESENTER_CLSID, IUNKNOWN_GUID, out comObject);
            }
            else
            {
                exception = new Exception(string.Format("{0} bit processes are unsupported", ProcessBits));
                goto bottom;
            }

            /* Check if our call to our DLL failed */
            if(hr != 0 || comObject == null)
            {
                exception = new COMException("Could not create a new class factory.", hr);
                goto bottom;
            }
            
            /* Cast the COM object that was returned to a COM interface type */
            factory = comObject as IClassFactory;
            
            if(factory == null)
            {
                exception = new Exception("Could not QueryInterface for the IClassFactory interface");
                goto bottom;
            }

            /* Get the GUID of the IMFVideoPresenter */
            Guid guidActivate = typeof(IMFActivate).GUID;
            object comActObject;
            
            /* Creates a new instance of the IMFVideoPresenter */
            factory.CreateInstance(null, ref guidActivate, out comActObject);
            
            // Set the Unknown IMFAttribute to ActivateObject
            activate.SetUnknown(MFAttributesClsid.MF_ACTIVATE_CUSTOM_VIDEO_PRESENTER_ACTIVATE, comActObject);    

            /* QueryInterface for the IMFVideoPresenter */
            var presenter = comActObject as IMFVideoPresenter;

            /* QueryInterface for our callback registration interface */
            var registerCb = comActObject as IEVRPresenterRegisterCallback;

            if(registerCb == null)
            {
                exception = new Exception("Could not QueryInterface for IEVRPresenterRegisterCallback");
                goto bottom;
            }
            
            /* Register the callback to the 'helper' class we created */
            registerCb.RegisterCallback(evrPresenter);

            /* Populate the IMFVideoPresenter */
            evrPresenter.VideoPresenter = presenter;
             
            bottom:

            if(factory != null)
                Marshal.FinalReleaseComObject(factory);

            if(exception != null)
                throw exception;

            return evrPresenter;
        }

        #region Event Invokers
        /// <summary>
        /// Fires the NewAllocatorFrame event
        /// </summary>
        private void InvokeNewAllocatorFrame()
        {
            var newAllocatorFrameAction = NewAllocatorFrame;
            if(newAllocatorFrameAction != null) newAllocatorFrameAction();
        }

        /// <summary>
        /// Fires the NewAlloctorSurface event
        /// </summary>
        /// <param name="pSurface">D3D surface pointer</param>
        private void InvokeNewAllocatorSurface(IntPtr pSurface)
        {
            var del = NewAllocatorSurface;
            if(del != null) del(this, pSurface);
        }
        #endregion
    }
}