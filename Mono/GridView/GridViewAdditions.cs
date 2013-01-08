using System;

using MonoTouch.UIKit;
using MonoTouch.CoreAnimation;
using MonoTouch.CoreGraphics;
using MonoTouch.Foundation;
using MonoTouch.ObjCRuntime;
using System.Collections.Generic;

namespace GridView
{
	public static class GridViewAdditions
	{
		public static void ShakeStatus(this UIView self,bool enabled)		
		{
			if (enabled) 
			{
				float rotation = 0.03f;

				CABasicAnimation shake = CABasicAnimation.FromKeyPath("transform");
				shake.Duration = 0.13;
				shake.AutoReverses = true;
				shake.RepeatCount  = float.MaxValue;
				shake.RemovedOnCompletion = false;
				shake.From = NSValue.FromCATransform3D(self.Layer.Transform.Rotate(-rotation,0,0,1));
				shake.To  = NSValue.FromCATransform3D(self.Layer.Transform.Rotate(rotation,0,0,1));
				self.Layer.AddAnimation(shake,"shakeAnimation");
			}
			else
			{
				self.Layer.RemoveAnimation("shakeAnimation");
			}
		}

		public delegate void RecursiveEnumerateSubviewsBlock(UIView view, out bool stop);
		public static void RecursiveEnumerateSubviewsUsingBlock(this UIView self,RecursiveEnumerateSubviewsBlock block)
		{
			if (self.Subviews.Length==0)
				return;

			foreach (UIView subView in self.Subviews)
			{
				bool stop = false;
				block(subView,out stop);
				if (stop)
					return;
				subView.RecursiveEnumerateSubviewsUsingBlock(block);
			}
		}

		public delegate void EnumerateGridCellBlock(GridViewCell cell,out bool stop);
		public static void EnumerateGridCells(this NSArray array,EnumerateGridCellBlock block)
		{
			GridViewCell[] list = NSArray.FromArray<GridViewCell>(array);
			foreach (GridViewCell cell in list)
			{
				bool stop = false;
				block(cell,out stop);
				if (stop)
					return;
			}
		}

		public static void End(this UIGestureRecognizer self)		
		{
			bool currentStatus = self.Enabled;
			self.Enabled = false;
			self.Enabled = currentStatus;
		}

		public static bool HasRecognizedValidGesture(this UIGestureRecognizer self)		
		{
			return (self.State == UIGestureRecognizerState.Changed || self.State == UIGestureRecognizerState.Began);
		}

		public static void RemoveAll<T>(this HashSet<T> self)
		{
			self.Clear();
		}

		public static bool IsLandscape(this UIInterfaceOrientation self)
		{
			if (self==UIInterfaceOrientation.LandscapeLeft || self==UIInterfaceOrientation.LandscapeRight)
				return true;
			return false;
		}

		public static void ReplaceObjectAtIndex<T>(this List<T> self,int index,T obj)
		{
			self.RemoveAt(index);
			self.Insert(index,obj);
		}
	}
}
