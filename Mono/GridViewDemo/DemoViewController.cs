using System;

using MonoTouch.UIKit;
using MonoTouch.Foundation;
using MonoTouch.ObjCRuntime;

using GridView;
using System.Drawing;
using System.Collections.Generic;

namespace GridViewDemo
{
	public class DemoViewController : UIViewController,GridViewDataSource,GridViewSortingDelegate,GridViewTransformationDelegate,GridViewActionDelegate
	{
		public const int NUMBER_ITEMS_ON_LOAD=250;
		public const int NUMBER_ITEMS_ON_LOAD2=30;

		GMGridView gmGridView;
		UINavigationController optionsNav;
		UIPopoverController optionsPopOver;
		
		List<String> data;
		List<String>  data2;
		List<String>  currentData;
		int lastDeleteItemIndexAsked;

		#region Constructors

		public DemoViewController () : base(null,null)
		{
			CommonInit();
		}

		public DemoViewController (IntPtr handle) : base(handle)
		{
			CommonInit();
		}
		
		[Export("initWithCoder:")]
		public DemoViewController (NSCoder coder) : base(coder)
		{
			CommonInit();
		}
		
		public DemoViewController (String nibName,NSBundle bundle) : base(nibName, bundle)
		{
			CommonInit();
		}

		private static bool IsIpad
		{
			get
			{
				return true;
			}
		}

		private static bool IsIphone
		{
			get 
			{
				return !IsIpad;
			}
		}

		private void CommonInit()
		{
			Title = "Demo 1";
			
			UIBarButtonItem addButton = new UIBarButtonItem(UIBarButtonSystemItem.Add,this,new Selector("addMoreItem"));

			UIBarButtonItem space = new UIBarButtonItem(UIBarButtonSystemItem.FixedSpace,null,null);
			space.Width = 10;
			
			UIBarButtonItem removeButton = new UIBarButtonItem(UIBarButtonSystemItem.Trash,this,new Selector("removeItem"));

			UIBarButtonItem space2 = new UIBarButtonItem(UIBarButtonSystemItem.FixedSpace,null,null);
			space2.Width = 10;
			
			UIBarButtonItem refreshButton = new UIBarButtonItem(UIBarButtonSystemItem.Refresh,this,new Selector("refreshItem"));

			if (IsIpad)
				NavigationItem.LeftBarButtonItems = new UIBarButtonItem[]{addButton, space, removeButton, space2, refreshButton};
			else
				NavigationItem.LeftBarButtonItem = addButton;
						
			UIBarButtonItem optionsButton = new UIBarButtonItem(UIBarButtonSystemItem.Refresh,this,new Selector("refreshGrid"));

			if (IsIpad)			
				NavigationItem.RightBarButtonItems = new UIBarButtonItem[]{optionsButton};
			else
				NavigationItem.RightBarButtonItem = optionsButton;

			data = new List<String> ();
			
			for (int i = 0; i < NUMBER_ITEMS_ON_LOAD; i ++) 
			{
				data.Add (String.Format("A {0}",i));
			}
			
			data2 = new List<String> ();
			
			for (int i = 0; i < NUMBER_ITEMS_ON_LOAD2; i ++) 
			{
				data2.Add (String.Format("B {0}",i));
			}
			
			currentData = data;
		}

		#endregion

		#region LifeCycle

		public override void LoadView ()
		{
			base.LoadView ();
		
			View.BackgroundColor = UIColor.White;
						
			int spacing = IsIphone ? 10 : 15;
			
			GMGridView aGridView = new GMGridView(View.Bounds);				
			aGridView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
			aGridView.BackgroundColor = UIColor.Clear;
			gmGridView = aGridView;
			View.AddSubview(gmGridView);
			
			gmGridView.Style = GridViewStyle.Swap;
			gmGridView.ItemSpacing = spacing;
			gmGridView.MinEdgeInsets = new UIEdgeInsets(spacing, spacing, spacing, spacing);
			gmGridView.CenterGrid = true;
			gmGridView.ActionDelegate = this;
			gmGridView.SortingDelegate = this;
			gmGridView.TransformDelegate = this;
			gmGridView.DataSource = this;
			
			UIButton infoButton = new UIButton(UIButtonType.InfoDark);				
			infoButton.Frame = new RectangleF(View.Bounds.Size.Width - 40, 
			                                  View.Bounds.Size.Height - 40, 
			                              40,
			                              40);
			infoButton.AutoresizingMask = UIViewAutoresizing.FlexibleLeftMargin | UIViewAutoresizing.FlexibleTopMargin;
			infoButton.AddTarget(this,new Selector("presentInfo"),UIControlEvent.TouchUpInside);
			View.AddSubview(infoButton);
			
			UISegmentedControl dataSegmentedControl = new UISegmentedControl(new String[]{"DataSet 1","DataSet 2"});
			dataSegmentedControl.SizeToFit();

			dataSegmentedControl.Frame = new RectangleF(5, 
			                                            View.Bounds.Size.Height - dataSegmentedControl.Bounds.Size.Height - 5,
			                                            dataSegmentedControl.Bounds.Size.Width, 
			                                            dataSegmentedControl.Bounds.Size.Height);
			dataSegmentedControl.AutoresizingMask = UIViewAutoresizing.FlexibleRightMargin | UIViewAutoresizing.FlexibleTopMargin;
			dataSegmentedControl.TintColor = UIColor.Green;		
			dataSegmentedControl.SelectedSegment = 0;
			dataSegmentedControl.AddTarget(this,new Selector("dataSetChange:"),UIControlEvent.ValueChanged);
			View.AddSubview(dataSegmentedControl);

			/*
			OptionsViewController *optionsController = [[OptionsViewController alloc] init];
			optionsController.gridView = gmGridView;
			optionsController.contentSizeForViewInPopover = CGSizeMake(400, 500);
			
			_optionsNav = [[UINavigationController alloc] initWithRootViewController:optionsController];
			
			if (INTERFACE_IS_PHONE)
			{
				UIBarButtonItem *doneButton = [[UIBarButtonItem alloc] initWithTitle:@"Done" style:UIBarButtonItemStyleDone target:self action:@selector(optionsDoneAction)];
				optionsController.navigationItem.rightBarButtonItem = doneButton;
			}*/
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();
			gmGridView.MainSuperView = NavigationController.View; //[UIApplication sharedApplication].keyWindow.rootViewController.view;
		}

		public override void ViewDidUnload()
		{
			base.ViewDidUnload();
			gmGridView = null;
		}

		public override bool ShouldAutorotateToInterfaceOrientation(UIInterfaceOrientation interfaceOrientation)
		{
			return true;
		}

		#endregion

		#region GMGridViewDataSource implementation
		
		public int NumberOfItemsInGridView (GMGridView gridView)
		{
			return (int)currentData.Count;
		}
		
		public SizeF GridViewSizeForItemsInInterfaceOrientation (GMGridView gridView, UIInterfaceOrientation orientation)
		{
			if (IsIphone)
			{
				if (orientation.IsLandscape())				
				{
					return new SizeF(170, 135);
				}
				else
				{
					return new SizeF(140, 110);
				}
			}
			else
			{
				if (orientation.IsLandscape()) 
				{
					return new SizeF(285, 205);
				}
				else
				{
					return new SizeF(230, 175);
				}
			}
		}
		
		public GridViewCell GridViewCellForItemAtIndex (GMGridView gridView, int index)
		{
			//NSLog(@"Creating view indx %d", index);
			
			SizeF size = GridViewSizeForItemsInInterfaceOrientation(gridView,UIApplication.SharedApplication.StatusBarOrientation);
			
			GridViewCell cell = gridView.DequeueReusableCell();				
			
			if (cell==null) 
			{
				cell = new GridViewCell();
				cell.setDeleteButtonIcon(null);
				cell.setDeleteButtonOffset(new PointF(-15, -15));
				
				UIView view = new UIView(new RectangleF(0,0,size.Width,size.Height));					
				view.BackgroundColor = UIColor.Red;
				view.Layer.MasksToBounds = false;
				view.Layer.CornerRadius = 8;
				
				cell.ContentView = view;
			}
			else
			{
				RectangleF f = cell.Frame;
				f.Size = size;
				cell.Frame = f;
				cell.Alpha = 1.0f;
			}
			cell.RemoveContentViewSubviews();

			UILabel label = new UILabel(cell.ContentView.Bounds);				
			label.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
			label.Text = currentData[index];
			label.TextAlignment = UITextAlignment.Center;
			label.BackgroundColor = UIColor.Clear;
			label.TextColor = UIColor.Black;
			label.HighlightedTextColor = UIColor.White;
			label.Font = UIFont.BoldSystemFontOfSize(20.0f);				
			cell.ContentView.AddSubview(label);
			
			return cell;
		}
		
		public bool GridViewCanDeleteItemAtIndex (GMGridView gridView, int index)
		{
			return true;
		}
		
		#endregion

		#region GMGridViewActionDelegate implementation
		
		public void GridViewDidTapOnItemAtIndex (GMGridView gridView, int position)
		{
			System.Console.WriteLine("Did tap at index {0}", position);
		}
		
		public void GridViewDidTapOnEmptySpace (GMGridView gridView)
		{
			System.Console.WriteLine("Tap on empty space");
		}
		
		public void GridViewProcessDeleteActionForItemAtIndex (GMGridView gridView, int index)
		{
			//UIAlertView *alert = [[UIAlertView alloc] initWithTitle:@"Confirm" message:@"Are you sure you want to delete this item?" delegate:self cancelButtonTitle:@"Cancel" otherButtonTitles:@"Delete", nil];
			
			//[alert show];
			
			//_lastDeleteItemIndexAsked = index;
		}

		/*
		- (void)alertView:(UIAlertView *)alertView clickedButtonAtIndex:(NSInteger)buttonIndex
		{
			if (buttonIndex == 1) 
			{
				[_currentData removeObjectAtIndex:_lastDeleteItemIndexAsked];
				[_gmGridView removeObjectAtIndex:_lastDeleteItemIndexAsked withAnimation:GMGridViewItemAnimationFade];
			}
		}*/

		public void GridViewChangeEdit (GMGridView gridView, bool edit)
		{
			
		}
		
		#endregion
		
		#region GMGridViewTransformationDelegate implementation
		
		public SizeF GridViewSizeInFullSizeForCell (GMGridView gridView, GridViewCell cell, int index, UIInterfaceOrientation orientation)
		{
			if (IsIphone) 
			{
				if (orientation.IsLandscape())
				{
					return new SizeF(320, 210);
				}
				else
				{
					return new SizeF(300, 310);
				}
			}
			else
			{
				if (orientation.IsLandscape())
				{
					return new SizeF(700, 530);
				}
				else
				{
					return new SizeF(600, 500);
				}
			}
		}
		
		public UIView GridViewFullSizeViewForCell (GMGridView gridView, GridViewCell cell, int index)
		{
			UIView fullView = new UIView();
			fullView.BackgroundColor = UIColor.Yellow;
			fullView.Layer.MasksToBounds = false;
			fullView.Layer.CornerRadius = 8.0f;
			
			SizeF size = GridViewSizeInFullSizeForCell(gridView,cell,index,UIApplication.SharedApplication.StatusBarOrientation);
			fullView.Bounds = new RectangleF(0, 0, size.Width, size.Height);
			
			UILabel label = new UILabel(fullView.Bounds);				
			label.Text = String.Format("Fullscreen View for cell at index {0}", index);				
			label.TextAlignment = UITextAlignment.Center;
			label.BackgroundColor = UIColor.Clear;
			label.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
			
			if (IsIphone) 
			{
				label.Font = UIFont.BoldSystemFontOfSize(15.0f);
			}
			else
			{
				label.Font = UIFont.BoldSystemFontOfSize(20.0f);
			}

			fullView.AddSubview(label);

			return fullView;
		}
		
		public void GridViewDidStartTransformingCell (GMGridView gridView, GridViewCell cell)
		{
			
		}
		
		public void GridViewDidEnterFullSizeForCell (GMGridView gridView, GridViewCell cell)
		{
			
		}
		
		public void GridViewDidEndTransformingCell (GMGridView gridView, GridViewCell cell)
		{
			
		}
		
		#endregion
		

		
		#region GMGridViewSortingDelegate implementation
		
		public void GridViewMoveItemAtIndex (GMGridView gridView, int oldIndex, int newIndex)
		{
			
		}
		
		public void GridViewExchangeItemAtIndex (GMGridView gridView, int index1, int index2)
		{
			
		}
		
		public void GridViewDidStartMovingCell (GMGridView gridView, GridViewCell cell)
		{
			
		}
		
		public void GridViewDidEndMovingCell (GMGridView gridView, GridViewCell cell)
		{
			
		}
		
		public bool GridViewShouldAllowShakingBehaviorWhenMovingCell (GMGridView gridView, GridViewCell view, int index)
		{
			return true;
		}
		
		#endregion

		#region Private Methods

		public int RandomNumber()
		{
			Random random = new Random();
			return random.Next (0,1000);
		}

		[Export("refreshGrid")]
		public void RefreshGrid()
		{
			gmGridView.ReloadData();
		}

		[Export("addMoreItem")]
		public void AddAnotherItem()
		{

			// Example: adding object at the last position
			String newItem = String.Format("{0}", RandomNumber());

			currentData.Add(newItem);

			gmGridView.InsertObjectAtIndex(currentData.Count-1,GridViewItemAnimation.Fade | GridViewItemAnimation.Scroll);
		}

		[Export("removeItem")]
		public void RemoveItem()
		{
			// Example: removing last item
			if (currentData.Count>0)			     
			{
				int index = currentData.Count - 1;

				gmGridView.RemoveObjectAtIndex(index,GridViewItemAnimation.Fade | GridViewItemAnimation.Scroll);

				currentData.RemoveAt(index);
			}
		}

		[Export("refreshItem")]
		public void RefreshItem()
		{
			// Example: reloading last item
			if (currentData.Count > 0) 
			{
				int index = currentData.Count - 1;
				
				String newMessage = String.Format ("{0}", RandomNumber());

				currentData.ReplaceObjectAtIndex(index,newMessage);
				gmGridView.ReloadObjectAtIndex(index,GridViewItemAnimation.Fade | GridViewItemAnimation.Scroll);

			}
		}

		[Export("dataSetChange:")]
		public void DataSetChanged(UISegmentedControl control)		
		{
			currentData = (control.SelectedSegment == 0) ? data : data2;

			gmGridView.ReloadData();
		}

		#endregion
	}
}

