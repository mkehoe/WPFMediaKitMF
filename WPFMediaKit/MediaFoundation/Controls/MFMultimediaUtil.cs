using System;
using System.Collections.Generic;
using System.Linq;
//using DirectShowLib;
using MediaFoundation;
using MediaFoundation.Misc;

namespace MediaFoundation.Controls
{
    public class MFDevice
    {
        public MFDevice(){}

        public string DevicePath { get; set; }
        //public IMoniker Mon { get; }
        public string Name { get; set; }

        public void Dispose(){}
        
        //public static MFDevice[] GetDevicesOfCat(Guid FilterCategory);
    }
    public class MultimediaUtil
    {
		#region Audio Renderer Methods
		/// <summary>
		/// The private cache of the audio renderer names
		/// </summary>
		private static string[] m_audioRendererNames;

		/// <summary>
		/// An array of audio renderer device names
		/// on the current system
		/// </summary>
		public static string[] AudioRendererNames
		{
			get
			{
				if (m_audioRendererNames == null)
				{
                    m_audioRendererNames = (from a in GetDevices(CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID)
					                        select a.Name).ToArray();
				}
				return m_audioRendererNames;
			}
		}
		#endregion

		#region Video Input Devices
		/// <summary>
		/// The private cache of the video input names
		/// </summary>
		private static string[] m_videoInputNames;

		/// <summary>
		/// An array of video input device names
		/// on the current system
		/// </summary>
		public static string[] VideoInputNames
		{
			get
			{
				if (m_videoInputNames == null)
				{
					m_videoInputNames = (from d in VideoInputDevices
										 select d.Name).ToArray();
				}
				return m_videoInputNames;
			}
		}

		#endregion

		private static MFDevice[] GetDevices(Guid filterCategory)
		{
            IMFAttributes pAttributes = null;
            IMFActivate[] ppDevices = null;

            // Create an attribute store to specify the enumeration parameters.
            int hr = MFExtern.MFCreateAttributes(out pAttributes, 1);
            MFError.ThrowExceptionForHR(hr);

            //CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID
            hr = pAttributes.SetGUID(MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, filterCategory);
            MFError.ThrowExceptionForHR(hr);

            int count;
            hr = MFExtern.MFEnumDeviceSources(pAttributes, out ppDevices, out count);

            MFDevice[] devices = new MFDevice[count];

            for (int i = 0; i < count; i++ )
            {
                int ssize = -1;
                string friendlyname = "";
                hr = ppDevices[i].GetAllocatedString(MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, out friendlyname, out ssize);

                int ssizesym = -1;
                string symlink;
                hr = ppDevices[i].GetAllocatedString(MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, out symlink, out ssizesym);
                //Use this attribute as input to the MFCreateDeviceSourceActivate function.

                devices[i] = new MFDevice();
                devices[i].Name = friendlyname;
                devices[i].DevicePath = symlink;
            }

            return devices;
            
		}

    	public static MFDevice[] VideoInputDevices
		{
			get
			{
				if (m_videoInputDevices == null)
				{
                    m_videoInputDevices = GetDevices(CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
				}
				return m_videoInputDevices;
			}
		}
		private static MFDevice[] m_videoInputDevices;

		public static string[] VideoInputsDevicePaths
		{
			get
			{
				if (m_videoInputsDevicePaths == null)
				{
					m_videoInputsDevicePaths = (from d in VideoInputDevices
					                          select d.DevicePath).ToArray();
				}
				return m_videoInputsDevicePaths;
			}
		}
		private static string[] m_videoInputsDevicePaths;
    }
}

