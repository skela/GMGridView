using System;

using MonoTouch.UIKit;

namespace Grid
{
	public class GridViewConstants
	{
		public static int GMGV_INVALID_POSITION
		{
			get
			{
				return -1;
			}
		}

		private static bool hasCheckedDeviceType=false;
		private static bool isIpad = false;

		public static bool IsIpad
		{
			get
			{
				if (!hasCheckedDeviceType)
				{
					isIpad = UIDevice.CurrentDevice.UserInterfaceIdiom==UIUserInterfaceIdiom.Pad;
					hasCheckedDeviceType = true;
				}
				return isIpad;
			}
		}
		
		public static bool IsIphone
		{
			get 
			{
				return !IsIpad;
			}
		}
	}
}
