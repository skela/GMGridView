using System;
using System.Drawing;

using MonoTouch.UIKit;
using MonoTouch.ObjCRuntime;
using MonoTouch.Foundation;

namespace Grid
{
	public class GridViewCell : UIView
	{
		private UIView contentView;         // The contentView - default is nil
		public String reuseIdentifier;

		bool inShakingMode;
		public bool IsInShakingMode
		{
			get
			{
				return inShakingMode;
			}
		}

		bool inFullSizeMode;
		public bool IsInFullSizeMode
		{
			get
			{
				return inFullSizeMode;
			}
		}

		public UIViewAutoresizing defaultFullsizeViewResizingMask;
		public UIButton deleteButton;



		#region Constructors

		public GridViewCell () : base(new RectangleF())
		{
			Prep();
		}

		[Export("initWithFrame:")]
		public GridViewCell (RectangleF frame) : base(frame)
		{
			Prep();
		}

		[Export("initWithCoder:")]
		public GridViewCell (NSCoder coder) : base(coder)
		{
			Prep();
		}

		public GridViewCell (IntPtr handle) : base(handle)
		{
			Prep();
		}

		private void Prep()
		{
			AutosizesSubviews = !true;
			editing = false;
			
			UIButton delButton = new UIButton(UIButtonType.Custom);				
			deleteButton = delButton;
			deleteButton.SetTitleColor(UIColor.Black,UIControlState.Normal);
			DeleteButtonIcon = null;
			DeleteButtonOffset=new PointF(-5,-5);
			deleteButton.Alpha = 0.0f;
			AddSubview(deleteButton);
			deleteButton.AddTarget(this,new Selector("actionDelete"),UIControlEvent.TouchUpInside);
		}

		#endregion

		#region UIView

		public override void LayoutSubviews ()
		{
			base.LayoutSubviews ();

			if(inFullSizeMode)
			{
				PointF origin = new PointF((Bounds.Size.Width - fullSize.Width) / 2, 
				                           (Bounds.Size.Height - fullSize.Height) / 2);
				if (fullSizeView!=null)
					fullSizeView.Frame = new RectangleF(origin.X, origin.Y, fullSize.Width, fullSize.Height);
			}
			else
			{
				if (fullSizeView!=null)
					fullSizeView.Frame = Bounds;
			}
		}

		public override void TouchesBegan (MonoTouch.Foundation.NSSet touches, UIEvent evt)
		{
			base.TouchesBegan (touches, evt);

			highlighted = true;
		}

		public override void TouchesEnded (MonoTouch.Foundation.NSSet touches, UIEvent evt)
		{
			base.TouchesEnded (touches, evt);
			highlighted = false;
		}

		public override void TouchesCancelled (MonoTouch.Foundation.NSSet touches, UIEvent evt)
		{
			base.TouchesCancelled (touches, evt);
			highlighted = false;
		}

		public void RemoveContentViewSubviews ()
		{
			foreach (UIView v in contentView.Subviews)
				v.RemoveFromSuperview();
		}

		#endregion

		#region Getters / Setters

		public UIView ContentView
		{
			get
			{
				return contentView;
			}
			set
			{
				Shake(false);
				
				if(contentView!=null)
				{
					contentView.RemoveFromSuperview();
					value.Frame = contentView.Frame;
				}
				else
				{
					value.Frame = Bounds;
				}
				
				contentView = value;
				
				contentView.AutoresizingMask = UIViewAutoresizing.None;
				AddSubview(this.contentView);
				BringSubviewToFront(deleteButton);
			}
		}

		private UIView fullSizeView;

		public UIView FullSizeView
		{
			set
			{
				if (IsInFullSizeMode) 
				{
					if (fullSizeView!=null)
					{
						if (value!=null)
						{
							value.Frame = fullSizeView.Frame;
							value.Alpha = fullSizeView.Alpha;
						}
					}
					else
					{
						if (value!=null)
						{
							value.Frame = Bounds;
							value.Alpha = 0;
						}
					}
				}
				else
				{
					if (value!=null)
					{
						value.Frame = Bounds;
						value.Alpha = 0;
					}
				}

				if (value!=null)
					defaultFullsizeViewResizingMask = value.AutoresizingMask | UIViewAutoresizing.FlexibleLeftMargin | UIViewAutoresizing.FlexibleRightMargin | UIViewAutoresizing.FlexibleTopMargin | UIViewAutoresizing.FlexibleBottomMargin;
				
				if (fullSizeView!=null)
				{
					if (value!=null)
						value.AutoresizingMask = fullSizeView.AutoresizingMask;			
					fullSizeView.RemoveFromSuperview();
				}
				
				fullSizeView = value;
				if (fullSizeView!=null)
					AddSubview(fullSizeView);
				BringSubviewToFront(deleteButton);
			}
			get
			{
				return fullSizeView;
			}
		}

		private SizeF fullSize;
		public SizeF FullSize
		{
			set
			{
				fullSize = value;
				SetNeedsLayout();
			}
			get
			{
				return fullSize;
			}
		}

		private bool editing;
		public bool IsEditing
		{
			get
			{
				return editing;
			}
			set
			{
				SetEditing(value,false);
			}
		}

		public void SetEditing(bool isEditing,bool animated)
		{
			if (isEditing != editing) 
			{
				editing = isEditing;
				if (animated) 
				{
					UIView.Animate(0.2,0,UIViewAnimationOptions.AllowUserInteraction|UIViewAnimationOptions.CurveEaseInOut,delegate{deleteButton.Alpha = editing ? 1.0f : 0.0f;},delegate{});
				}
				else 
				{
					deleteButton.Alpha = editing ? 1.0f : 0.0f;
				}
				
				contentView.UserInteractionEnabled = !editing;
				this.ShakeStatus(editing);
			}
		}
		
		public PointF DeleteButtonOffset
		{
			get
			{
				return deleteButton.Frame.Location;
			}
			set
			{
				PointF offset = value;
				deleteButton.Frame = new RectangleF(offset.X,offset.Y,deleteButton.Frame.Size.Width,deleteButton.Frame.Size.Height);
			}
		}

		public UIImage DeleteButtonIcon
		{
			set
			{
				UIImage newDeleteButtonIcon = value;

				deleteButton.SetImage(newDeleteButtonIcon,UIControlState.Normal);
				
				if (newDeleteButtonIcon!=null) 
				{
					deleteButton.Frame = new RectangleF(deleteButton.Frame.X, 
					                                     deleteButton.Frame.Y, 
					                                    newDeleteButtonIcon.Size.Width, 
					                                    newDeleteButtonIcon.Size.Height);
					deleteButton.SetTitle(null,UIControlState.Normal);
					deleteButton.BackgroundColor = UIColor.Clear;
				}
				else
				{
					deleteButton.Frame = new RectangleF(deleteButton.Frame.X, 
					                                     deleteButton.Frame.Y, 
					                                     35, 
					                                     35);

					deleteButton.SetTitle("X",UIControlState.Normal);
					deleteButton.BackgroundColor  = UIColor.LightGray;
				}
			}
			get
			{
				return deleteButton.CurrentImage;
			}
		}

		private bool highlighted;
		public bool IsHighlighted
		{
			get
			{
				return highlighted;
			}
			set
			{
				highlighted = value;

				Selector sel = new Selector("setHighlighted:");
				NSNumber isHigh = NSNumber.FromBoolean(highlighted);
				contentView.RecursiveEnumerateSubviewsUsingBlock(
				delegate(UIView view,out bool stop)
				{
					if (view.RespondsToSelector(sel))
					{
						view.PerformSelector(sel,isHigh,0);
						//((UIControl)view).Highlighted = highlighted;
					}
				});
			}
		}

		#endregion

		#region Private Methods
		
		public delegate void DeleteBlockDelegate(GridViewCell cell);
		
		public DeleteBlockDelegate DeleteBlock;
		
		[Export("actionDelete")]
		private void ActionDelete()
		{
			if (DeleteBlock!=null)
			{
				DeleteBlock(this);
			}
		}
		
		#endregion

		#region Public Methods

		public virtual void PrepareForReuse()
		{
			fullSize = new SizeF();
			fullSizeView = null;
			editing = false;
			DeleteBlock = null;
		}

		public void Shake(bool on)
		{
			if ((on && !inShakingMode) || (!on && inShakingMode)) 
			{
				contentView.ShakeStatus(on);
				inShakingMode = on;
			}
		}
		
		public void SwitchToFullSizeMode(bool fullSizeEnabled)
		{
			if (fullSizeEnabled) 
			{
				fullSizeView.AutoresizingMask = defaultFullsizeViewResizingMask;
				
				PointF center = fullSizeView.Center;
				fullSizeView.Frame = new RectangleF(fullSizeView.Frame.X, fullSizeView.Frame.Y, fullSize.Width, fullSize.Height);
				fullSizeView.Center = center;
				
				inFullSizeMode = true;
				
				fullSizeView.Alpha = (float)Math.Max(fullSizeView.Alpha, contentView.Alpha);
				contentView.Alpha  = 0.0f;

				UIView.Animate(0.3,
				delegate
				 {
					fullSizeView.Alpha = 1.0f;
					fullSizeView.Frame = new RectangleF(fullSizeView.Frame.X, fullSizeView.Frame.Y, fullSize.Width, fullSize.Height);
					fullSizeView.Center = center;
				 },
				delegate
				 {
					SetNeedsLayout();
				 });
			}
			else
			{
				fullSizeView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
				
				inFullSizeMode = false;
				fullSizeView.Alpha = 0.0f;
				contentView.Alpha  = 0.6f;

				UIView.Animate(0.3,
				delegate
				{				
					contentView.Alpha  = 1.0f;
					fullSizeView.Frame = Bounds;
				},
				delegate
				{
					SetNeedsLayout(); 
				
				 }
				);
			}
		}
		
		public void StepToFullsizeWithAlpha(float alpha)
		{
			return; // not supported anymore - to be fixed
			
			if (!IsInFullSizeMode)
			{
				alpha = (float) Math.Max(0, alpha);
				alpha = (float) Math.Min(1, alpha);
				
				fullSizeView.Alpha = alpha;
				contentView.Alpha  = 1.4f - alpha;
			}
		}

		#endregion

	}
}
