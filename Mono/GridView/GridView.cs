using System;
using MonoTouch.UIKit;
using System.Drawing;
using MonoTouch.Foundation;
using MonoTouch.ObjCRuntime;
using MonoTouch.CoreAnimation;
using MonoTouch.CoreFoundation;
using System.Collections.Generic;
using System.Diagnostics;
using MonoTouch.CoreGraphics;

namespace Grid
{
	public enum GridViewStyle
	{
		Push=0,
		Swap
	}
	
	public enum GridViewScrollPosition
	{
		None,
		Top,
		Middle,
		Bottom
	}
	
	public enum GridViewItemAnimation
	{
		None=0,
		Fade,
		Scroll= 1<<7
	}

	#region GridViewDataSource
	
	public interface GridViewDataSource
	{			
		//@required
		int NumberOfItemsInGridView(GridView gridView);
		SizeF GridViewSizeForItemsInInterfaceOrientation(GridView gridView,UIInterfaceOrientation orientation);
		GridViewCell GridViewCellForItemAtIndex(GridView gridView,int index);
		//@optional
		bool GridViewCanDeleteItemAtIndex(GridView gridView,int index); // Allow a cell to be deletable. If not implemented, YES is assumed.
	}
	
	#endregion
	
	#region GridViewActionDelegate
	
	public interface GridViewActionDelegate
	{
		//@required
		void GridViewDidTapOnItemAtIndex(GridView gridView,int position);
		
		//@optional
		// Tap on space without any items
		void GridViewDidTapOnEmptySpace(GridView gridView);
		// Called when the delete-button has been pressed. Required to enable editing mode.
		// This method wont delete the cell automatically. Call the delete method of the gridView when appropriate.
		void GridViewProcessDeleteActionForItemAtIndex(GridView gridView,int index);
		void GridViewChangeEdit(GridView gridView,bool edit);
	}
	
	#endregion
	
	#region GridViewSortingDelegate
	
	public interface GridViewSortingDelegate
	{
		//@required
		// Item moved - right place to update the data structure
		void GridViewMoveItemAtIndex(GridView gridView,int oldIndex,int newIndex);
		void GridViewExchangeItemAtIndex(GridView gridView,int index1,int index2);		
		
		//@optional
		// Sorting started/ended - indexes are not specified on purpose (not the right place to update data structure)
		void GridViewDidStartMovingCell(GridView gridView,GridViewCell cell);
		void GridViewDidEndMovingCell(GridView gridView,GridViewCell cell);
		// Enable/Disable the shaking behavior of an item being moved
		bool GridViewShouldAllowShakingBehaviorWhenMovingCell(GridView gridView,GridViewCell view,int index);
	}
	
	#endregion
	
	#region GridViewTransformationDelegate
	
	public interface GridViewTransformationDelegate
	{
		//@required
		// Fullsize
		SizeF GridViewSizeInFullSizeForCell(GridView gridView,GridViewCell cell,int index,UIInterfaceOrientation orientation);
		UIView GridViewFullSizeViewForCell(GridView gridView,GridViewCell cell,int index);

		// Transformation (pinch, drag, rotate) of the item
		//@optional
		void GridViewDidStartTransformingCell(GridView gridView,GridViewCell cell);
		void GridViewDidEnterFullSizeForCell(GridView gridView,GridViewCell cell);
		void GridViewDidEndTransformingCell(GridView gridView,GridViewCell cell);
	}
	
	#endregion

	public class GridView : UIScrollView
	{
		// Constants
		public int GMGV_INVALID_POSITION = GridViewConstants.GMGV_INVALID_POSITION;
		public const int kTagOffset = 50;
		public const float kDefaultAnimationDuration = 0.3f;
		public const UIViewAnimationOptions kDefaultAnimationOptions = UIViewAnimationOptions.BeginFromCurrentState | UIViewAnimationOptions.AllowUserInteraction;

		public GridView(IntPtr handle) : base(handle)
		{
			CommonInit ();
		}
		
		[Export("initWithCoder:")]
		public GridView (NSCoder coder) : base(coder)
		{
			CommonInit ();
		}

		[Export("initWithFrame:")]
		public GridView (RectangleF rect) : base(rect)
		{
			CommonInit ();
		}

		GridViewLayoutStrategy layoutStrategy;
		UIView mainSuperView;
		bool editing;
		int itemSpacing;
		GridViewStyle style;
		double minimumPressDuration;
		bool showFullSizeViewWithAlphaWhenTransforming;
		UIEdgeInsets minEdgeInsets;
		int sortFuturePosition;
		SizeF itemSize;
		bool centerGrid;
		float lastScale;
		float lastRotation;
		PointF minPossibleContentOffset;
		PointF maxPossibleContentOffset;
		//HashSet<GMGridViewCell> reusableCells;
		NSMutableSet reusableCells;
		GridViewCell transformingItem;
		bool inFullSizeMode;
		bool inTransformingState;
		bool itemsSubviewsCacheIsValid;
		int firstPositionLoaded;
		int lastPositionLoaded;
		int numberTotalItems;
		GridViewCell sortMovingItem;

		GridViewDataSource dataSource;  					// Required
		GridViewActionDelegate actionDelegate;            // Optional - to get taps callback & deleting item
		GridViewSortingDelegate sortingDelegate;          // Optional - to enable sorting
		GridViewTransformationDelegate transformDelegate; // Optional - to enable fullsize mode

		UITapGestureRecognizer tapGesture;
		UIPinchGestureRecognizer pinchGesture;
		UIRotationGestureRecognizer rotationGesture;
		UIPanGestureRecognizer panGesture;
		UIPanGestureRecognizer sortingPanGesture;
		UILongPressGestureRecognizer longPressGesture;

		GridGestureRecognizer recog;

		private void CommonInit()
		{
			recog = new GridGestureRecognizer(this);

			tapGesture = new UITapGestureRecognizer(this,new Selector("tapGestureUpdated:"));
			tapGesture.Delegate = recog;
			tapGesture.NumberOfTapsRequired = 1;
			tapGesture.NumberOfTouchesRequired = 1;
			tapGesture.CancelsTouchesInView = false;
			AddGestureRecognizer(tapGesture);

			/////////////////////////////
			// Transformation gestures :
			pinchGesture = new UIPinchGestureRecognizer(this,new Selector("pinchGestureUpdated:"));				
			pinchGesture.Delegate = recog;
			AddGestureRecognizer(pinchGesture);
			
			rotationGesture = new UIRotationGestureRecognizer(this,new Selector("rotationGestureUpdated:"));				
			rotationGesture.Delegate = recog;
			AddGestureRecognizer(rotationGesture);
			
			panGesture = new UIPanGestureRecognizer(this,new Selector("panGestureUpdated:"));				
			panGesture.Delegate = recog;
			panGesture.MaximumNumberOfTouches=2;
			panGesture.MinimumNumberOfTouches=2;
			AddGestureRecognizer(panGesture);

			//////////////////////
			// Sorting gestures :			
			sortingPanGesture = new UIPanGestureRecognizer(this,new Selector("sortingPanGestureUpdated:"));				
			sortingPanGesture.Delegate = recog;
			AddGestureRecognizer(sortingPanGesture);
			
			longPressGesture = new UILongPressGestureRecognizer(this,new Selector("longPressGestureUpdated:"));				
			longPressGesture.NumberOfTouchesRequired = 1;				
			longPressGesture.Delegate = recog;
			longPressGesture.CancelsTouchesInView = false;
			AddGestureRecognizer(longPressGesture);

			////////////////////////
			// Gesture dependencies
			UIPanGestureRecognizer panGestureRecognizer = null;
			if (this.RespondsToSelector(new Selector("panGestureRecognizer")))// iOS5 only			
			{ 
				panGestureRecognizer = this.PanGestureRecognizer;
			}
			else 
			{
				foreach (UIGestureRecognizer gestureRecognizer in GestureRecognizers)
				{ 
					//if ([gestureRecognizer  isKindOfClass:NSClassFromString(@"UIScrollViewPanGestureRecognizer")]) 
					if (gestureRecognizer.ClassHandle.ToString().Equals("UIScrollViewPanGestureRecognizer")) // TODO: Test this!
					{
						panGestureRecognizer = (UIPanGestureRecognizer) gestureRecognizer;
					}
				}
			}
			panGestureRecognizer.MaximumNumberOfTouches = 1;
			panGestureRecognizer.RequireGestureRecognizerToFail(sortingPanGesture);
			//layoutStrategy = GMGridViewLayoutStrategyFactory.StrategyFromType(GMGridViewLayoutStrategyType.Vertical);				
			SetLayoutStrategy(GridViewLayoutStrategyFactory.StrategyFromType(GridViewLayoutStrategyType.Vertical));
			
			mainSuperView = this;
			editing = false;
			itemSpacing = 10;
			style = GridViewStyle.Swap;
			minimumPressDuration = 0.2;
			showFullSizeViewWithAlphaWhenTransforming = true;
			minEdgeInsets = new UIEdgeInsets(5, 5, 5, 5);
			ClipsToBounds = false;
			
			sortFuturePosition = GMGV_INVALID_POSITION;
			itemSize = new SizeF();
			centerGrid = true;
			
			lastScale = 1.0f;
			lastRotation = 0.0f;
			
			minPossibleContentOffset = new PointF(0,0);
			maxPossibleContentOffset = new PointF(0,0);
			
			//reusableCells = new HashSet<GMGridViewCell>();
			reusableCells = new NSMutableSet();

			NSNotificationCenter.DefaultCenter.AddObserver(this,new Selector("receivedMemoryWarningNotification:"),UIApplication.DidReceiveMemoryWarningNotification,null);
			NSNotificationCenter.DefaultCenter.AddObserver(this,new Selector("receivedWillRotateNotification:"),UIApplication.WillChangeStatusBarOrientationNotification,null);
		}

		#region Layout

		public static int NSMaxRange(NSRange range)
		{
			return (range.Location + range.Length);
		}

		public static bool NSLocationInRange(int loc, NSRange range) 
		{
			uint loci = (uint)loc;
			uint rloc=(uint)range.Location;
			uint rlen=(uint)range.Length;
			return ( (loci - rloc) < rlen);
		}

		bool CGRectIntersectsRect (RectangleF rect1, RectangleF rect2)
		{
			return rect1.IntersectsWith(rect2);
		}

		bool CGPointEqualToPoint (PointF newContentOffset, PointF oldContentOffset)
		{
			return newContentOffset.Equals(oldContentOffset);
		}

		public delegate void AnimationBlock();

		private void ApplyWithoutAnimation(AnimationBlock animations)
		{
			if (animations!=null) 
			{
				CATransaction.Begin();
				CATransaction.SetValueForKey(new NSNumber(true),CATransaction.DisableActionsKey);
				animations();
				CATransaction.Commit();
			}
		}

		public void LayoutSubviewsWithAnimation(GridViewItemAnimation animation)
		{
			RecomputeSizeAnimated(animation!=GridViewItemAnimation.None);
				//!(animation & GMGridViewItemAnimation.None));
			RelayoutItemsAnimated(animation==GridViewItemAnimation.Fade);  // only supported animation for now
			LoadRequiredItems();
		}

		public override void LayoutSubviews()
		{
			base.LayoutSubviews();
			
			if (rotationActive) 
			{
				rotationActive = false;
				
				// Updating all the items size
				SizeF newItemSize = dataSource.GridViewSizeForItemsInInterfaceOrientation(this,UIApplication.SharedApplication.StatusBarOrientation);

				if (!newItemSize.Equals(itemSize)) 
				{
					itemSize = newItemSize;

					ItemSubviews.EnumerateGridCells(delegate(GridViewCell cell,out bool stop)
					{
						stop=false;
						if (cell != transformingItem) 
						{
							cell.Bounds = new RectangleF(0, 0, itemSize.Width, itemSize.Height);
							cell.ContentView.Frame = cell.Bounds;
						}
					});
				}
				
				// Updating the fullview size
				
				if (transformingItem!=null && inFullSizeMode) 
				{
					int position = transformingItem.Tag - kTagOffset;
					SizeF fullSize = transformDelegate.GridViewSizeInFullSizeForCell(this,transformingItem,position,UIApplication.SharedApplication.StatusBarOrientation);						

					if (!fullSize.Equals(transformingItem.FullSize)) 
					{
						PointF center = transformingItem.FullSizeView.Center;
						transformingItem.FullSize = fullSize;
						transformingItem.FullSizeView.Center = center;
					}
				}
				
				// Adding alpha animation to make the relayouting more smooth
				
				CATransition transition = CATransition.CreateAnimation();					
				transition.Duration = 0.25f;
				transition.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
				transition.Type = CATransition.TransitionFade;
				Layer.AddAnimation(transition,"rotationAnimation");

				ApplyWithoutAnimation(delegate{LayoutSubviewsWithAnimation(GridViewItemAnimation.None);});

				// Fixing the contentOffset when pagging enabled
				
				if (PagingEnabled) 
				{
					SetContentOffset(RectForPoint(ContentOffset,true).Location,true);
				}
			}
			else 
			{
				LayoutSubviewsWithAnimation(GridViewItemAnimation.None);
			}
		}

		#endregion

		#region Orientation and memory management

		[Export("receivedMemoryWarningNotification:")]
		public void ReceivedMemoryWarningNotification(NSNotification notification)
		{
			CleanupUnseenItems();
			reusableCells.RemoveAll();
		}

		bool rotationActive;

		[Export("receivedWillRotateNotification:")]
		public void ReceivedWillRotateNotification(NSNotification notification)
		{
			rotationActive = true;
		}

		#endregion

		#region Setters / Getters

		public GridViewStyle Style
		{
			get
			{
				return style;
			}
			set
			{
				style = value;
			}
		}

		public GridViewDataSource DataSource
		{
			set
			{
				dataSource = value;
				ReloadData();
			}
			get
			{
				return dataSource;
			}
		}

		public GridViewTransformationDelegate TransformDelegate
		{
			set
			{
				transformDelegate = value;
				//ReloadData();
			}
			get
			{
				return transformDelegate;
			}
		}

		public GridViewActionDelegate ActionDelegate
		{
			set
			{
				actionDelegate = value;
				//ReloadData();
			}
			get
			{
				return actionDelegate;
			}
		}

		public GridViewSortingDelegate SortingDelegate
		{
			set
			{
				sortingDelegate = value;
				//ReloadData();
			}
			get
			{
				return sortingDelegate;
			}
		}

		public UIView MainSuperView
		{
			set
			{
				mainSuperView = value != null ? value : this;
			}
			get
			{
				return mainSuperView;
			}
		}

		public override void SetNeedsLayout()
		{
			base.SetNeedsLayout();
		}

		public void SetLayoutStrategy(GridViewLayoutStrategy newLayoutStrategy)
		{
			layoutStrategy = newLayoutStrategy;
			PagingEnabled = layoutStrategy.RequiresEnablingPaging();
			SetNeedsLayout();
		}
		
		public int ItemSpacing
		{
			set
			{
				itemSpacing = value;
				SetNeedsLayout();
			}
			get
			{
				return itemSpacing;
			}
		}
		
		public bool CenterGrid
		{
			set
			{
				centerGrid = value;
				SetNeedsLayout();
			}
			get
			{
				return centerGrid;
			}
		}
		
		public UIEdgeInsets MinEdgeInsets
		{
			set
			{
				minEdgeInsets = value;
				SetNeedsLayout();
			}
			get
			{
				return minEdgeInsets;
			}
		}

		public double MinimumPressDuration
		{
			get
			{
				return longPressGesture.MinimumPressDuration;
			}
			set
			{
				longPressGesture.MinimumPressDuration = value;
			}
		}

		public void SetEditing(bool shouldEdit)
		{
			SetEditing(shouldEdit,false);
			if (actionDelegate!=null)
			{
				actionDelegate.GridViewChangeEdit(this,shouldEdit);
			}
		}
		
		public void SetEditing(bool shouldEdit,bool animated)
		{
			if (actionDelegate!=null && !IsInTransformingState && ((editing && !shouldEdit) || (!editing && shouldEdit)))
			{
				ItemSubviews.EnumerateGridCells(delegate(GridViewCell cell,out bool stop)
				{
					stop=false;
					int index = PositionForItemSubview(cell);
					if (index != GMGV_INVALID_POSITION)
					{
						bool allowEdit = shouldEdit && dataSource.GridViewCanDeleteItemAtIndex(this,index);	
						cell.SetEditing(allowEdit,animated);
					}
				});
				editing = shouldEdit;
			}
		}

		#endregion

		#region UIScrollView Delegate Replacement

		public override PointF ContentOffset
		{
			set
			{
				bool valueChanged = !CGPointEqualToPoint(value,ContentOffset);

				base.ContentOffset = value;

				if (valueChanged) 
				{
					LoadRequiredItems();
				}
			}
			get
			{
				return base.ContentOffset;
			}
		}

		#endregion

		#region UIGestureRegocnizer Delegate

		bool enableEditOnLongPress;

		public bool ShouldBegin (UIGestureRecognizer gestureRecognizer)
		{
			bool valid = true;
			bool isScrolling = Dragging || Decelerating;
			
			if (gestureRecognizer == tapGesture) 
			{
				if (editing && disableEditOnEmptySpaceTap) 
				{
					PointF locationTouch = tapGesture.LocationInView(this);
					int position = layoutStrategy.ItemPositionFromLocation(locationTouch);
					
					valid = (position == GMGV_INVALID_POSITION);
				}
				else 
				{
					valid = !isScrolling && !editing && !longPressGesture.HasRecognizedValidGesture();
				}
			}
			else if (gestureRecognizer == longPressGesture)
			{
				valid = (sortingDelegate!=null || enableEditOnLongPress) && !isScrolling && !editing;
			}
			else if (gestureRecognizer == sortingPanGesture) 
			{
				valid = (sortMovingItem != null && longPressGesture.HasRecognizedValidGesture());
			}
			else if(gestureRecognizer == rotationGesture || gestureRecognizer == pinchGesture || gestureRecognizer == panGesture)
			{
				if (transformDelegate != null && gestureRecognizer.NumberOfTouches == 2) 
				{
					PointF locationTouch1 = gestureRecognizer.LocationOfTouch(0,this);
					PointF locationTouch2 = gestureRecognizer.LocationOfTouch(1,this);
					
					int positionTouch1 = layoutStrategy.ItemPositionFromLocation(locationTouch1);
	                int positionTouch2 = layoutStrategy.ItemPositionFromLocation(locationTouch2);
					
					valid = !editing && (IsInTransformingState || ((positionTouch1 == positionTouch2) && (positionTouch1 != GMGV_INVALID_POSITION)));
				}
				else
				{
					valid = false;
				}
			}
			
			return valid;
		}

		private class GridGestureRecognizer : UIGestureRecognizerDelegate
		{
			GridView gridView;

			public GridGestureRecognizer(GridView gridView) : base()
			{
				this.gridView = gridView;
			}

			public override bool ShouldRecognizeSimultaneously (UIGestureRecognizer gestureRecognizer, UIGestureRecognizer otherGestureRecognizer)
			{
				return true;
			}

			public override bool ShouldBegin (UIGestureRecognizer recognizer)
			{
				return gridView.ShouldBegin (recognizer);
			}
		}

		#endregion

		#region Sorting Gestures And Logic

		[Export("longPressGestureUpdated:")]
		public void LongPressGestureUpdated(UILongPressGestureRecognizer longPressGesture)
		{
			if (enableEditOnLongPress && !editing)
			{
				PointF locationTouch = longPressGesture.LocationInView(this);
				int position = layoutStrategy.ItemPositionFromLocation(locationTouch);
				
				if (position != GMGV_INVALID_POSITION) 
				{
					if (!editing)
					{
						editing = true;
					}
				}
				return;
			}
			
			switch (longPressGesture.State) 
			{
				case UIGestureRecognizerState.Began:
				{
					if (sortMovingItem==null) 
					{ 
						PointF location = longPressGesture.LocationInView(this);
						
						int position = layoutStrategy.ItemPositionFromLocation(location);
						
						if (position != GMGV_INVALID_POSITION) 
						{
							SortingMoveDidStartAtPoint(location);
						}
					}
					
					break;
				}
				case UIGestureRecognizerState.Ended:
				case UIGestureRecognizerState.Cancelled:
				case UIGestureRecognizerState.Failed:
				{
					sortingPanGesture.End();
					
					if (sortMovingItem!=null) 
					{                
						PointF location = longPressGesture.LocationInView(this);
						SortingMoveDidStopAtPoint(location);
					}
					
					break;
				}
				default:
					break;
			}
		}

		[Export("sortingPanGestureUpdated:")]
		public void SortingPanGestureUpdated(UIPanGestureRecognizer panGesture)
		{
			switch (panGesture.State) 
			{
				case UIGestureRecognizerState.Ended:
				case UIGestureRecognizerState.Cancelled:
				case UIGestureRecognizerState.Failed:
				{
					autoScrollActive = false;
					break;
				}
				case UIGestureRecognizerState.Began:
				{            
					autoScrollActive = true;
					SortingAutoScrollMovementCheck();
										
					break;
				}
				case UIGestureRecognizerState.Changed:
				{
					PointF translation = panGesture.TranslationInView(this);						
					PointF offset = translation;
					PointF locationInScroll = panGesture.LocationInView(this);
					sortMovingItem.Transform = CGAffineTransform.MakeTranslation(offset.X, offset.Y);
					SortingMoveDidContinueToPoint(locationInScroll);

					break;
				}
				default:
					break;
			}
		}

		bool autoScrollActive;

		[Export("sortingAutoScrollMovementCheck")]
		public void SortingAutoScrollMovementCheck()
		{
			if (sortMovingItem!=null && autoScrollActive) 
			{
				PointF locationInMainView = sortingPanGesture.LocationInView(this);					
				locationInMainView = new PointF(locationInMainView.X - ContentOffset.X,
				                                 locationInMainView.Y - ContentOffset.Y
				                                 );
				
				
				float threshhold = itemSize.Height;
				PointF offset = ContentOffset;
				PointF locationInScroll = sortingPanGesture.LocationInView(this);
				
				// Going down
				if (locationInMainView.X + threshhold > Bounds.Size.Width) 
				{            
					offset.X += itemSize.Width / 2;
					
					if (offset.X > maxPossibleContentOffset.X) 
					{
						offset.X = maxPossibleContentOffset.X;
					}
				}
				// Going up
				else if (locationInMainView.X - threshhold <= 0) 
				{            
					offset.X -= itemSize.Width / 2;
					
					if (offset.X < minPossibleContentOffset.X) 
					{
						offset.X = minPossibleContentOffset.X;
					}
				}

				// Going right
				if (locationInMainView.Y + threshhold > Bounds.Size.Height) 
				{            
					offset.Y += itemSize.Height / 2;
					
					if (offset.Y > maxPossibleContentOffset.Y) 
					{
						offset.Y = maxPossibleContentOffset.Y;
					}
				}
				// Going left
				else if (locationInMainView.Y - threshhold <= 0) 
				{
					offset.Y -= itemSize.Height / 2;
					
					if (offset.Y < minPossibleContentOffset.Y) 
					{
						offset.Y = minPossibleContentOffset.Y;
					}
				}
				
				if (offset.X != ContentOffset.X || offset.Y != ContentOffset.Y) 
				{
					UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
					delegate
					{
						ContentOffset = offset;
					},
					delegate
					{
						ContentOffset = offset;
						
						if (autoScrollActive) 
						{
							SortingMoveDidContinueToPoint(locationInScroll);
						}

						SortingAutoScrollMovementCheck();
					});
				}
				else
				{
					PerformSelector(new Selector("sortingAutoScrollMovementCheck"),null,0.5);
				}
			}
		}
		
		void SortingMoveDidStartAtPoint(PointF point)
		{
			int position = layoutStrategy.ItemPositionFromLocation(point);
			
			GridViewCell item = CellForItemAtIndex(position);
			BringSubviewToFront(item);
			sortMovingItem = item;

			RectangleF frameInMainView = ConvertRectToView(sortMovingItem.Frame,mainSuperView);

			sortMovingItem.RemoveFromSuperview();
			sortMovingItem.Frame = frameInMainView;
			mainSuperView .AddSubview(sortMovingItem);

			sortFuturePosition = sortMovingItem.Tag - kTagOffset;
			sortMovingItem.Tag = 0;
			
			if (sortingDelegate!=null)
			{
				sortingDelegate.GridViewDidStartMovingCell(this,sortMovingItem);
			}
			
			if (sortingDelegate!=null)
			{
				sortMovingItem.Shake(sortingDelegate.GridViewShouldAllowShakingBehaviorWhenMovingCell(this,sortMovingItem,position));			
			}
			else
			{
				sortMovingItem.Shake(true);				
			}
		}
		
		void SortingMoveDidStopAtPoint(PointF point)
		{
			sortMovingItem.Shake(false);
			sortMovingItem.Tag = sortFuturePosition + kTagOffset;
			 
			RectangleF frameInScroll = mainSuperView.ConvertRectToView(sortMovingItem.Frame,this);

			sortMovingItem.RemoveFromSuperview();
			sortMovingItem.Frame=frameInScroll;
			AddSubview(sortMovingItem);

			PointF newOrigin = layoutStrategy.OriginForItemAtPosition(sortFuturePosition);
			RectangleF newFrame = new RectangleF(newOrigin.X, newOrigin.Y, itemSize.Width, itemSize.Height);

			UIView.Animate(kDefaultAnimationDuration,0,0,
			delegate
			{
				sortMovingItem.Transform = CGAffineTransform.MakeIdentity();
				sortMovingItem.Frame = newFrame;
			},
			delegate
			{
				if (sortingDelegate!=null)
				{
					sortingDelegate.GridViewDidEndMovingCell(this,sortMovingItem);
				}
				
				sortMovingItem = null;
				sortFuturePosition = GMGV_INVALID_POSITION;

				SetSubviewsCacheAsInvalid();
			});
		}
		
		void SortingMoveDidContinueToPoint(PointF point)
		{
			int position = layoutStrategy.ItemPositionFromLocation(point);
			int tag = position + kTagOffset;
			
			if (position != GMGV_INVALID_POSITION && position != sortFuturePosition && position < numberTotalItems) 
			{
				bool positionTaken = false;
				
				ItemSubviews.EnumerateGridCells(delegate(GridViewCell v,out bool stop)
				{
					stop = false;
					if (v != sortMovingItem && v.Tag == tag) 
					{
						positionTaken = true;
						stop = true;
					}
				});
				
				if (positionTaken)
				{
					switch (style) 
					{
						case GridViewStyle.Push:
						{
							if (position > sortFuturePosition) 
							{
								ItemSubviews.EnumerateGridCells(delegate(GridViewCell v,out bool stop)
								{
									stop=false;
									if ((v.Tag == tag || (v.Tag < tag && v.Tag >= sortFuturePosition + kTagOffset)) && v != sortMovingItem ) 
									{
										v.Tag = v.Tag - 1;
										SendSubviewToBack(v);
									}
								});
							}
							else
							{
								ItemSubviews.EnumerateGridCells(delegate(GridViewCell v,out bool stop)
								{
									stop=false;
									if ((v.Tag == tag || (v.Tag > tag && v.Tag <= sortFuturePosition + kTagOffset)) && v != sortMovingItem) 
									{
										v.Tag = v.Tag + 1;
										SendSubviewToBack(v);
									}
								});
							}

							sortingDelegate.GridViewMoveItemAtIndex(this,sortFuturePosition,position);
							RelayoutItemsAnimated(true);
							break;
						}
						case GridViewStyle.Swap:
						default:
						{
							if (sortMovingItem!=null) 
							{
								UIView v = CellForItemAtIndex(position);
								v.Tag = sortFuturePosition + kTagOffset;
								PointF origin = layoutStrategy.OriginForItemAtPosition(sortFuturePosition);

								UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
								delegate
				               	{
									v.Frame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
								},
								delegate
								{

								});
							}

							sortingDelegate.GridViewExchangeItemAtIndex(this,sortFuturePosition,position);

							break;
						}
					}
				}
				
				sortFuturePosition = position;
			}
		}

		#endregion

		#region TapGesture

		bool disableEditOnEmptySpaceTap;

		[Export("tapGestureUpdated:")]
		public void TapGestureUpdated(UITapGestureRecognizer tapGesture_)
		{
			PointF locationTouch = tapGesture.LocationInView(this);				
			int position = layoutStrategy.ItemPositionFromLocation(locationTouch);
			
			if (position != GMGV_INVALID_POSITION) 
			{
				if (!editing) 
				{
					var cell = CellForItemAtIndex(position);
					if (cell!=null)
					{
						cell.IsHighlighted = false;
						if (actionDelegate!=null)
							actionDelegate.GridViewDidTapOnItemAtIndex(this,position);
					}
				}
			}
			else
			{ 
				if (actionDelegate!=null)
				{
					actionDelegate.GridViewDidTapOnEmptySpace(this);
				}
				
				if (disableEditOnEmptySpaceTap) 
				{
					editing = false;
				}
			}
		}

		#endregion

		#region Private Methods

		private void SetSubviewsCacheAsInvalid()
		{
			itemsSubviewsCacheIsValid = false;
		}

		private GridViewCell NewItemSubViewForPosition(int position)
		{
			GridViewCell cell = dataSource.GridViewCellForItemAtIndex(this,position);				
			PointF origin = layoutStrategy.OriginForItemAtPosition(position);
			RectangleF frame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
			
			// To make sure the frame is not animated
			ApplyWithoutAnimation(delegate
			{
				cell.Frame = frame;
				cell.ContentView.Frame = cell.Bounds;
			});

			cell.Tag = position + kTagOffset;
			bool canEdit = editing && dataSource.GridViewCanDeleteItemAtIndex(this,position);
			cell.SetEditing(canEdit,animated:false);
			
			GridView weakSelf = this;
			cell.DeleteBlock = delegate(GridViewCell aCell)
			{
				int index = weakSelf.PositionForItemSubview(aCell);
				if (index != GMGV_INVALID_POSITION) 
				{
					bool canDelete = weakSelf.dataSource.GridViewCanDeleteItemAtIndex(weakSelf,index);						
					if (canDelete)
					{
						weakSelf.actionDelegate.GridViewProcessDeleteActionForItemAtIndex(weakSelf,index);
					}
				}
			};
			
			return cell;
		}
		/*
		List<GMGridViewCell>itemSubviewsCache;
		object subviewsLocker=new object();
		public List<GMGridViewCell>ItemSubviews
		{
			get
			{
				List<GMGridViewCell>subviews = null;
				
				if (itemsSubviewsCacheIsValid) 
				{
					subviews = new List<GMGridViewCell>();
					subviews.AddRange(itemSubviewsCache);
				}
				else
				{
					lock(subviewsLocker)					
					{
						List<GMGridViewCell> itemSubViews = new List<GMGridViewCell>(numberTotalItems);

						foreach (UIView  v in Subviews)
						{
							if (v is GMGridViewCell)
							{
								itemSubViews.Add ((GMGridViewCell)v);
							}
						}
						
						subviews = itemSubViews;
						
						itemSubviewsCache = new List<GMGridViewCell>();
						itemSubviewsCache.AddRange (subviews);
						itemsSubviewsCacheIsValid = true;
					}
				}

				Console.WriteLine ("subviews count is " + subviews.Count);

				return subviews;
			}
		}*/

		NSArray itemSubviewsCache;
		object subviewsLocker=new object();
		public NSArray ItemSubviews
		{
			get
			{
				NSArray subviews = null;

				if (itemsSubviewsCacheIsValid) 
				{
					subviews = (NSArray)itemSubviewsCache.Copy ();

					//Console.WriteLine ("subviews count is " + subviews.Count);
				}
				else
				{
					lock(subviewsLocker)					
					{
						NSMutableArray itemSubViews = new NSMutableArray(numberTotalItems);

						foreach (UIView  v in Subviews)
						{
							if (v is GridViewCell)
							{
								itemSubViews.Add (v);
							}
						}
						
						subviews = itemSubViews;

						itemSubviewsCache = (NSArray)subviews.Copy ();
						itemsSubviewsCacheIsValid = true;

						//Console.WriteLine ("NEW subviews count is " + subviews.Count);
					}
				}

				return subviews;
			}
		}

		private GridViewCell CellForItemAtIndex(int position)
		{
			GridViewCell view = null;

			ItemSubviews.EnumerateGridCells(delegate(GridViewCell v,out bool stop)
			{
				stop=false;
				if (v.Tag == position + kTagOffset) 
				{
					view = v;
					stop = true;
				}
			});

			return view;
		}

		private int PositionForItemSubview(GridViewCell view)
		{
			return view.Tag >= kTagOffset ? view.Tag - kTagOffset : GMGV_INVALID_POSITION;
		}

		private void RecomputeSizeAnimated(bool animated)
		{
			layoutStrategy.SetupItemSize(itemSize,itemSpacing,minEdgeInsets,centerGrid);
			layoutStrategy.RebaseWithItemCount(numberTotalItems,Bounds);

			SizeF contentSize = layoutStrategy.GetContentSize();

			minPossibleContentOffset = new PointF(0,0);
			maxPossibleContentOffset = new PointF(contentSize.Width - Bounds.Size.Width + ContentInset.Right,contentSize.Height - Bounds.Size.Height + ContentInset.Bottom);
			
			bool shouldUpdateScrollviewContentSize = !ContentSize.Equals(contentSize);				

			//Console.WriteLine("Should update contentsize: " + shouldUpdateScrollviewContentSize.ToString());

			if (shouldUpdateScrollviewContentSize)
			{
				if (animated)
				{
					UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,delegate
					{
						ContentSize = contentSize;
					},
					delegate
					{
					});
				}
				else
				{
					ContentSize = contentSize;
				}
			}			
		}

		private delegate void RelayoutBlock();

		private void RelayoutItemsAnimated(bool animated)
		{
			//Console.WriteLine ("Relayout animated " + animated);

			RelayoutBlock layoutBlock = delegate 
			{
				ItemSubviews.EnumerateGridCells(delegate(GridViewCell view,out bool stop)
				{
					stop=false;
					if (view != sortMovingItem && view != transformingItem) 
					{
						int index = view.Tag - kTagOffset;
						PointF origin = layoutStrategy.OriginForItemAtPosition(index);							
						RectangleF newFrame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
						
						// IF statement added for performance reasons (Time Profiling in instruments)
						if (!newFrame.Equals(view.Frame)) 
						{
							view.Frame = newFrame;
						}
					}
				});
			};
			
			if (animated) 
			{
				UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
				delegate
				{
					layoutBlock();
				},
				delegate
				{

				});
			}
			else 
			{
				layoutBlock();
			}
		}

		private RectangleF RectForPoint(PointF point,bool pagging)
		{
			RectangleF targetRect = new RectangleF();
			
			if (PagingEnabled) 
			{
				PointF originScroll = new PointF();
				
				SizeF pageSize =  new SizeF(Bounds.Size.Width  - ContentInset.Left - ContentInset.Right,
				                            Bounds.Size.Height - ContentInset.Top  - ContentInset.Bottom);
				
				float pageX = (float) Math.Ceiling((double) (point.X / pageSize.Width));
				float pageY = (float) Math.Ceiling((double) (point.Y / pageSize.Height));
				
				originScroll = new PointF(pageX * pageSize.Width, 
				                           pageY *pageSize.Height);
				
				/*
        while (originScroll.x + pageSize.width < point.x) 
        {
            originScroll.x += pageSize.width;
        }
        
        while (originScroll.y + pageSize.height < point.y) 
        {
            originScroll.y += pageSize.height;
        }
        */
				targetRect = new RectangleF(originScroll.X, originScroll.Y, pageSize.Width, pageSize.Height);
			}
			else 
			{
				targetRect = new RectangleF(point.X, point.Y, itemSize.Width, itemSize.Height);
			}
			
			return targetRect;
		}

		#endregion

		#region Transformation Gestures and Logic

		[Export("panGestureUpdated:")]
		public void PanGestureUpdated(UIPanGestureRecognizer panGesture)
		{
			switch (panGesture.State) 
			{
				case UIGestureRecognizerState.Ended:
				case UIGestureRecognizerState.Cancelled:
				case UIGestureRecognizerState.Failed:
				{
					// TODO:  wtf is this?
					//[NSObject cancelPreviousPerformRequestsWithTarget:self selector:@selector(transformingGestureDidFinish) object:nil];
					Selector sel = new Selector("transformingGestureDidFinish");
					PerformSelector(sel,null,0.1);

					ScrollEnabled = true;


				} break;
				case UIGestureRecognizerState.Began:
				{
					TransformingGestureDidBeginWithGesture(panGesture);

					ScrollEnabled = false;

				} break;
				case UIGestureRecognizerState.Changed:
				{
					if (panGesture.NumberOfTouches != 2) 
					{
						panGesture.End();
					}
					
					PointF translate = panGesture.TranslationInView(this);
					transformingItem.ContentView.Center = new PointF(transformingItem.ContentView.Center.X + translate.X, transformingItem.ContentView.Center.Y + translate.Y);
					panGesture.SetTranslation(new PointF(),this);


				} break;
				default:
				{
				} break;
			}
		}

		[Export("pinchGestureUpdated:")]
		public void PinchGestureUpdated(UIPinchGestureRecognizer pinchGesture)
		{
			switch (pinchGesture.State) 
			{
				case UIGestureRecognizerState.Ended:
				case UIGestureRecognizerState.Cancelled:
				case UIGestureRecognizerState.Failed:
				{
					// TODO: Wtf is this?
					//[NSObject cancelPreviousPerformRequestsWithTarget:self selector:@selector(transformingGestureDidFinish) object:nil];
					PerformSelector(new Selector("transformingGestureDidFinish"),null,0.1);
					

				} break;
				case UIGestureRecognizerState.Began:
				{
					TransformingGestureDidBeginWithGesture(pinchGesture);
				} break;
				case UIGestureRecognizerState.Changed:
				{
					NSNumber val = transformingItem.ContentView.Layer.ValueForKey(new NSString("transform.scale")) as NSNumber;
					float currentScale = val!=null ? val.FloatValue : 1.0f;
					float scale = 1 - (lastScale - pinchGesture.Scale);
					
					//todo: compute these scale factors dynamically based on ratio of thumbnail/fullscreen sizes
					const float kMaxScale = 3;
					const float kMinScale = 0.5f;
					
					scale = (float)Math.Min(scale, kMaxScale / currentScale);
					scale = (float)Math.Max(scale, kMinScale / currentScale);
					
					if (scale >= kMinScale && scale <= kMaxScale) 
					{
						CGAffineTransform currentTransform = transformingItem.ContentView.Transform;
						//CGAffineTransform newTransform = currentTransform.Scale(scale, scale);
						currentTransform.Scale(scale, scale);
						CGAffineTransform newTransform = currentTransform;
						currentTransform = transformingItem.ContentView.Transform;
						transformingItem.ContentView.Transform = newTransform;
						
						lastScale = pinchGesture.Scale;
						
						currentScale += scale;
						
						float alpha = 1 - (kMaxScale - currentScale);
						alpha = (float)Math.Max(0, alpha);
						alpha = (float)Math.Min(1, alpha);
						
						if (showFullSizeViewWithAlphaWhenTransforming && currentScale >= 1.5) 
						{
							transformingItem.StepToFullsizeWithAlpha(alpha);
						}
						
						transformingItem.BackgroundColor = UIColor.DarkGray.ColorWithAlpha((float)Math.Min(alpha, 0.9));							
					}
				} break;
				default:
				{
				} break;
			}
		}

		[Export("rotationGestureUpdated:")]
		public void RotationGestureUpdated(UIRotationGestureRecognizer rotationGesture)
		{
			switch (rotationGesture.State) 
			{
				case UIGestureRecognizerState.Ended:
				case UIGestureRecognizerState.Cancelled:
				case UIGestureRecognizerState.Failed:
				{
					// TODO: WTF is this?
					//[NSObject cancelPreviousPerformRequestsWithTarget:self selector:@selector(transformingGestureDidFinish) object:nil];
					PerformSelector(new Selector("transformingGestureDidFinish"),null,0.1);

				} break;
				case UIGestureRecognizerState.Began:
				{
					TransformingGestureDidBeginWithGesture(rotationGesture);
				} break;
				case UIGestureRecognizerState.Changed:
				{
					float rotation = rotationGesture.Rotation - lastRotation;
					CGAffineTransform currentTransform = transformingItem.ContentView.Transform;
					currentTransform.Rotate(rotation);
					CGAffineTransform newTransform = currentTransform;
					transformingItem.ContentView.Transform = newTransform;
					lastRotation = rotationGesture.Rotation;
					

				} break;
				default:
				{
				} break;
			}
		}

		void TransformingGestureDidBeginWithGesture(UIGestureRecognizer gesture)
		{
			inFullSizeMode = false;
			
			if (inTransformingState && gesture is UIPinchGestureRecognizer)			    
			{
				pinchGesture.Scale = 2.5f;
			}

			if (inTransformingState)
			{   
				inTransformingState = false;
				transformingItem.SwitchToFullSizeMode(false);					
				CGAffineTransform newTransform = CGAffineTransform.MakeScale(2.5f, 2.5f);
				transformingItem.ContentView.Transform = newTransform;
				transformingItem.ContentView.Center = transformingItem.FullSizeView.Center;
			}
			else if (transformingItem==null) 
			{        
				PointF locationTouch = gesture.LocationOfTouch(0,this);
				int positionTouch = layoutStrategy.ItemPositionFromLocation(locationTouch);
				TransformingGestureDidBeginAtPosition(positionTouch);
			}
		}

		[Export("transformingGestureDidFinish")]
		public void TransformingGestureDidFinish()
		{
			if (IsInTransformingState) 
			{
				if (lastScale > 2 && !inTransformingState) 
				{
					TransformingGestureDidEnd();
				}
				else if (!inTransformingState)
				{
					lastRotation = 0;
					lastScale = 1.0f;
					
					GridViewCell transformingView = transformingItem;
					transformingItem = null;
					
					int position = PositionForItemSubview(transformingView);						
					PointF origin = layoutStrategy.OriginForItemAtPosition(position);
					
					RectangleF finalFrameInScroll = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
					RectangleF finalFrameInSuperview = ConvertRectToView(finalFrameInScroll,mainSuperView);

					transformingView.SwitchToFullSizeMode(false);
					transformingView.AutoresizingMask = UIViewAutoresizing.None;

					UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
					delegate
					{
						transformingView.ContentView.Transform = CGAffineTransform.MakeIdentity();
						transformingView.ContentView.Frame = finalFrameInSuperview;
						transformingView.BackgroundColor = UIColor.Clear;
					},
					delegate
					{
						transformingView.RemoveFromSuperview();
						transformingView.Frame=finalFrameInScroll;
						transformingView.ContentView.Frame = transformingView.Bounds;
						AddSubview(transformingView);
												
						transformingView.FullSizeView = null;
						inFullSizeMode = false;

						transformDelegate.GridViewDidEndTransformingCell(this,transformingView);

						// Transfer the gestures back
						AddGestureRecognizer(pinchGesture);
						AddGestureRecognizer(rotationGesture);
						AddGestureRecognizer(panGesture);
					});
				}
			}
		}

		private void TransformingGestureDidBeginAtPosition(int position)
		{
			transformingItem = CellForItemAtIndex(position);
			if (transformingItem==null)
				return;
			RectangleF frameInMainView = ConvertRectToView(transformingItem.Frame,mainSuperView);
			
			transformingItem.RemoveFromSuperview();
			transformingItem.Frame = mainSuperView.Bounds;
			transformingItem.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
			transformingItem.ContentView.Frame = frameInMainView;
			mainSuperView.AddSubview(transformingItem);
			mainSuperView.BringSubviewToFront(transformingItem);
			
			transformingItem.FullSize = transformDelegate.GridViewSizeInFullSizeForCell(this,transformingItem,position,UIApplication.SharedApplication.StatusBarOrientation);
			transformingItem.FullSizeView = transformDelegate.GridViewFullSizeViewForCell(this,transformingItem,position);
			
			transformDelegate.GridViewDidStartTransformingCell(this,transformingItem);
		}
		
		private void TransformingGestureDidEnd()
		{
			lastRotation = 0;
			lastScale = 1;
			
			BringSubviewToFront(transformingItem);
			
			float rotationValue = (float)Math.Atan2(transformingItem.ContentView.Transform.xy,transformingItem.ContentView.Transform.xx);
			
			transformingItem.ContentView.Transform = CGAffineTransform.MakeIdentity();
			transformingItem.SwitchToFullSizeMode(true);
			
			transformingItem.BackgroundColor = UIColor.DarkGray.ColorWithAlpha(0.9f);
			
			transformingItem.FullSizeView.Transform =  CGAffineTransform.MakeRotation(rotationValue);
			
			UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
            delegate
            {
				transformingItem.FullSizeView.Transform = CGAffineTransform.MakeIdentity();
			},
			delegate
			{
				
			});
			
			inTransformingState = true;
			inFullSizeMode = true;
			
			transformDelegate.GridViewDidEnterFullSizeForCell(this,transformingItem);
			
			// Transfer the gestures on the fullscreen to make is they are accessible (depends on self.mainSuperView)
			transformingItem.FullSizeView.AddGestureRecognizer(pinchGesture);
			transformingItem.FullSizeView.AddGestureRecognizer(rotationGesture);
			transformingItem.FullSizeView.AddGestureRecognizer(panGesture);
		}
		
		public void DisplayFullScreenForPosition(int position)
		{
			if (transformingItem!=null)
			{
				transformingItem.SwitchToFullSizeMode(false);
				transformDelegate.GridViewDidEndTransformingCell(this,transformingItem);
				transformingItem=null;
				ReloadData();
			}
			TransformingGestureDidBeginAtPosition(position);
			TransformingGestureDidEnd();
		}

		private bool IsInTransformingState
		{
			get
			{
				return transformingItem != null;
			}
		}

		#endregion

		#region Loading / Destroying Items and Reusing Cells

		private void LoadRequiredItems()
		{
			NSRange rangeOfPositions = layoutStrategy.RangeOfPositionsInBoundsFromOffset(ContentOffset);
			NSRange loadedPositionsRange = new NSRange(firstPositionLoaded,lastPositionLoaded - firstPositionLoaded);

			//Console.WriteLine(@"Range of locs: "+rangeOfPositions.ToString());
			//Console.WriteLine(@"Range of loaded pos: "+loadedPositionsRange.ToString());


			// calculate new position range
			firstPositionLoaded = firstPositionLoaded == GMGV_INVALID_POSITION ? rangeOfPositions.Location : Math.Min(firstPositionLoaded, (int)rangeOfPositions.Location);
			lastPositionLoaded  = lastPositionLoaded == GMGV_INVALID_POSITION ? NSMaxRange(rangeOfPositions) : Math.Max(lastPositionLoaded, (int)(rangeOfPositions.Length + rangeOfPositions.Location));

			// remove now invisible items
			SetSubviewsCacheAsInvalid();
			CleanupUnseenItems();
			
			// add new cells
			bool forceLoad = (firstPositionLoaded == GMGV_INVALID_POSITION) || (lastPositionLoaded == GMGV_INVALID_POSITION);
			int positionToLoad;
			for (int i = 0; i < rangeOfPositions.Length; i++) 
			{
				positionToLoad = i + rangeOfPositions.Location;

				//Console.WriteLine("NSLocationInRange:" + NSLocationInRange(positionToLoad, loadedPositionsRange).ToString());

				if ((forceLoad || !NSLocationInRange(positionToLoad, loadedPositionsRange)) && positionToLoad < numberTotalItems) 
				{
					//Console.WriteLine("ps to load is " + positionToLoad);

					//Console.WriteLine ("I'm here");
					if (CellForItemAtIndex(positionToLoad)==null) 
					{
						//Console.WriteLine ("Added grid cell at pos: " + positionToLoad.ToString());
						GridViewCell cell = NewItemSubViewForPosition(positionToLoad);							
						AddSubview(cell);					
					}
				}
			}   

		}

		private void CleanupUnseenItems()
		{
			//int cleanupCounter=0;

			NSRange rangeOfPositions = layoutStrategy.RangeOfPositionsInBoundsFromOffset(ContentOffset);
			GridViewCell cell;
			
			if ((int)rangeOfPositions.Location > firstPositionLoaded) 
			{
				for (int i = firstPositionLoaded; i < (int)rangeOfPositions.Location; i++) 
				{
					cell = CellForItemAtIndex(i);
					if(cell!=null)
					{
						QueueReusableCell(cell);
						cell.RemoveFromSuperview();
						//cleanupCounter++;
					}
				}
				
				firstPositionLoaded = rangeOfPositions.Location;
				SetSubviewsCacheAsInvalid();
			}

			if ((int)NSMaxRange(rangeOfPositions) < lastPositionLoaded) 
			{
				for (int i = NSMaxRange(rangeOfPositions); i <= lastPositionLoaded; i++)
				{
					cell = CellForItemAtIndex(i);
					if(cell!=null)
					{
						QueueReusableCell(cell);
						cell.RemoveFromSuperview();
						//cleanupCounter++;
					}
				}

				//Console.WriteLine ("Cleaned up " + cleanupCounter);

				lastPositionLoaded = NSMaxRange(rangeOfPositions);
				SetSubviewsCacheAsInvalid();
			}
		}

		private void QueueReusableCell(GridViewCell cell)
		{
			if (cell!=null) 
			{
				cell.PrepareForReuse();
				cell.Alpha = 1;
				cell.BackgroundColor = UIColor.Clear;
				reusableCells.Add(cell);
			}
		}

		public GridViewCell DequeueReusableCell()
		{
			GridViewCell cell = (GridViewCell)reusableCells.AnyObject;
			
			if (cell!=null) 
			{
				reusableCells.Remove(cell);
			}
			
			return cell;
		}
		
		public GridViewCell DequeueReusableCellWithIdentifier(String identifier)
		{
			GridViewCell cell = null;

			foreach (GridViewCell reusableCell in reusableCells.ToArray<GridViewCell>())
			{
				if (identifier.Equals(reusableCell.reuseIdentifier))
				{
					cell = reusableCell;
					break;
				}
			}
			
			if (cell!=null) 
			{
				reusableCells.Remove(cell);
			}
			
			return cell;
		}


		#endregion



		#region Public Methods

		public void ReloadData()
		{
			PointF previousContentOffset = ContentOffset;

			ItemSubviews.EnumerateGridCells(delegate(GridViewCell obj,out bool stop)
			{
				stop=false;
				if (obj is GridViewCell)				    
				{
					obj.RemoveFromSuperview();
					QueueReusableCell((GridViewCell)obj);
				}
			});
			
			firstPositionLoaded = GMGV_INVALID_POSITION;
			lastPositionLoaded  = GMGV_INVALID_POSITION;

			SetSubviewsCacheAsInvalid();
			
			int numberItems = dataSource.NumberOfItemsInGridView(this);				
			itemSize = dataSource.GridViewSizeForItemsInInterfaceOrientation(this,UIApplication.SharedApplication.StatusBarOrientation);
			numberTotalItems = numberItems;

			RecomputeSizeAnimated(false);
			
			PointF newContentOffset = new PointF(Math.Min(maxPossibleContentOffset.X, previousContentOffset.X), Math.Min(maxPossibleContentOffset.Y, previousContentOffset.Y));
			newContentOffset = new PointF(Math.Max(newContentOffset.X, minPossibleContentOffset.X), Math.Max(newContentOffset.Y, minPossibleContentOffset.Y));
			
			ContentOffset = newContentOffset;
			
			LoadRequiredItems();
			
			SetSubviewsCacheAsInvalid();
			SetNeedsLayout();
		}
		
		public void ReloadObjectAtIndex(int index,bool animated)
		{
			ReloadObjectAtIndex(index,animated ? GridViewItemAnimation.Scroll : GridViewItemAnimation.None);
		}
		
		public void ReloadObjectAtIndex(int index,GridViewItemAnimation animation)
		{
			Debug.Assert((index >= 0 && index < numberTotalItems), "Invalid index");
			
			UIView currentView = CellForItemAtIndex(index);

			if (currentView==null)
				return;

			GridViewCell cell = NewItemSubViewForPosition(index);
			PointF origin = layoutStrategy.OriginForItemAtPosition(index);
			cell.Frame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
			cell.Alpha = 0;
			AddSubview(cell);
			
			currentView.Tag = kTagOffset - 1;
			bool shouldScroll = animation.HasFlag(GridViewItemAnimation.Scroll);
			bool animate = animation.HasFlag(GridViewItemAnimation.Fade);
			UIView.Animate(animate ? kDefaultAnimationDuration : 0.0,0.0,kDefaultAnimationOptions,
			delegate
			{
				if (shouldScroll) 
				{
					ScrollToObjectAtIndex(index,GridViewScrollPosition.None,false);
				}
				currentView.Alpha = 0;
				cell.Alpha = 1;
			},
			delegate
			{
				currentView.RemoveFromSuperview();
			});

			SetSubviewsCacheAsInvalid();
		}
		
		public void ScrollToObjectAtIndex(int index,GridViewScrollPosition scrollPosition,bool animated)
		{
			index = Math.Max(0, index);
			index = Math.Min(index,numberTotalItems);
			
			PointF origin = layoutStrategy.OriginForItemAtPosition(index);
			RectangleF targetRect = RectForPoint(origin,PagingEnabled);
			
			if (!PagingEnabled)
			{
				RectangleF gridRect = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
				
				switch (scrollPosition)
				{
					case GridViewScrollPosition.None:
					default:
						targetRect = gridRect; // no special coordinate handling
						break;
						
					case GridViewScrollPosition.Top:
						targetRect.Y = gridRect.Y;	// set target y origin to cell's y origin
						break;
						
					case GridViewScrollPosition.Middle:
						targetRect.Y = (float)Math.Max(gridRect.Y - (float)Math.Ceiling((targetRect.Size.Height - gridRect.Size.Height) * 0.5), 0.0);
						break;
						
					case GridViewScrollPosition.Bottom:
						targetRect.Y = (float)Math.Max((float)Math.Floor(gridRect.Y - (targetRect.Size.Height - gridRect.Size.Height)), 0.0);
						break;
				}
			}
			
			// Better performance animating ourselves instead of using animated:YES in scrollRectToVisible
			/*
			UIView.Animate(animated ? kDefaultAnimationDuration : 0,0,kDefaultAnimationOptions,
			delegate
			{
				ScrollRectToVisible(targetRect,false);
			},
			delegate
			{

			});*/
			ScrollRectToVisible(targetRect,animated);
		}
		
		public void InsertObjectAtIndex(int index,bool animated)
		{
			InsertObjectAtIndex(index,animated ? GridViewItemAnimation.Scroll : GridViewItemAnimation.None);
		}
		
		public void InsertObjectAtIndex(int index,GridViewItemAnimation animation)
		{
			Debug.Assert((index >= 0 && index <= numberTotalItems), "Invalid index specified");
			
			GridViewCell cell = null;
			
			if (index >= firstPositionLoaded && index <= lastPositionLoaded) 
			{        
				cell = NewItemSubViewForPosition(index);
				
				for (int i = numberTotalItems - 1; i >= index; i--)
				{
					UIView oldView = CellForItemAtIndex(i);
					oldView.Tag = oldView.Tag + 1;
				}
				
				if (animation == GridViewItemAnimation.Fade) 
				{
					cell.Alpha = 0;
					UIView.BeginAnimations(null);
					UIView.SetAnimationDelay(kDefaultAnimationDuration);
					UIView.SetAnimationDuration(kDefaultAnimationDuration);
					cell.Alpha = 1.0f;
					UIView.CommitAnimations();
				}
				AddSubview(cell);
			}
			
			numberTotalItems++;
			RecomputeSizeAnimated(animation!=GridViewItemAnimation.None);
			
			bool shouldScroll = animation.HasFlag(GridViewItemAnimation.Scroll);
			if (shouldScroll)
			{
				UIView.Animate (kDefaultAnimationDuration,0,kDefaultAnimationOptions,
				delegate
				{
					ScrollToObjectAtIndex(index,GridViewScrollPosition.None,false);
				},
				delegate
				{
					LayoutSubviewsWithAnimation(animation);				
				});
			}
			else 
			{
				LayoutSubviewsWithAnimation(animation);
			}

			SetSubviewsCacheAsInvalid();
		}

		public void RemoveObjectAtIndex(int index,bool animated)
		{
			RemoveObjectAtIndex(index,GridViewItemAnimation.None);
		}

		public void RemoveObjectAtIndex(int index,GridViewItemAnimation animation)
		{
			Debug.Assert((index >= 0 && index < numberTotalItems), "Invalid index specified");
			
			GridViewCell cell = CellForItemAtIndex(index);

			for (int i = index + 1; i < numberTotalItems; i++)
			{
				GridViewCell oldView = CellForItemAtIndex(i);
				oldView.Tag = oldView.Tag - 1;
			}

			if (cell!=null)
				cell.Tag = kTagOffset - 1;
			numberTotalItems--;

			bool shouldScroll = animation.HasFlag(GridViewItemAnimation.Scroll);
			bool animate = animation.HasFlag(GridViewItemAnimation.Fade);
			//bool shouldScroll = animation == GridViewItemAnimation.Scroll;
			//bool animate = animation == GridViewItemAnimation.Fade;

			UIView.Animate(animate ? kDefaultAnimationDuration : 0.0f,0,kDefaultAnimationOptions,
			delegate
			{
				if (cell!=null)
				{
					cell.ContentView.Alpha = 0.3f;
					cell.Alpha = 0.0f;
				}
				
				if (shouldScroll) 
				{
					ScrollToObjectAtIndex(index,GridViewScrollPosition.None,animate);
				}
				RecomputeSizeAnimated((animation != GridViewItemAnimation.None));
			},
			delegate
			{
				if (cell!=null)
					cell.ContentView.Alpha = 1.0f;
				QueueReusableCell(cell);
				if (cell!=null)
					cell.RemoveFromSuperview();

				firstPositionLoaded = lastPositionLoaded = GMGV_INVALID_POSITION;
				LoadRequiredItems();
				RelayoutItemsAnimated(animate);
			});

			SetSubviewsCacheAsInvalid();
		}

		public void SwapObjectAtIndex(int index1,int index2,bool animated)
		{
			SwapObjectAtIndex(index1,index2,animated ? GridViewItemAnimation.Scroll : GridViewItemAnimation.None);
		}

		public void SwapObjectAtIndex(int index1,int index2,GridViewItemAnimation animation)
		{
			Debug.Assert((index1 >= 0 && index1 < numberTotalItems), "Invalid index1 specified");
			Debug.Assert((index2 >= 0 && index2 < numberTotalItems), "Invalid index2 specified");
			
			GridViewCell view1 = CellForItemAtIndex(index1);
			GridViewCell view2 = CellForItemAtIndex(index2);
			
			view1.Tag = index2 + kTagOffset;
			view2.Tag = index1 + kTagOffset;
			
			PointF view1Origin = layoutStrategy.OriginForItemAtPosition(index2);
			PointF view2Origin = layoutStrategy.OriginForItemAtPosition(index1);
			
			view1.Frame = new RectangleF(view1Origin.X, view1Origin.Y, itemSize.Width, itemSize.Height);
			view2.Frame = new RectangleF(view2Origin.X, view2Origin.Y, itemSize.Width, itemSize.Height);
			
			
			RectangleF visibleRect = new RectangleF(ContentOffset.X,
			                                        ContentOffset.Y, 
			                                		ContentSize.Width, 
			                                		ContentSize.Height);
			
			// Better performance animating ourselves instead of using animated:YES in scrollRectToVisible
			bool shouldScroll = animation.HasFlag(GridViewItemAnimation.Scroll);				
			UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
			delegate
			{
				if (shouldScroll) 
				{
					if (!CGRectIntersectsRect(view2.Frame, visibleRect)) 
					{
						ScrollToObjectAtIndex(index1,GridViewScrollPosition.None,false);
					}
					else if (!CGRectIntersectsRect(view1.Frame, visibleRect)) 
					{
						ScrollToObjectAtIndex(index2,GridViewScrollPosition.None,false);
					}
				}
			},
			delegate
			{
				SetNeedsLayout();
			});
		}
		
		#endregion

		#region Deprecated Public Methods

		public UIScrollView ScrollView
		{
			get
			{
				return this;
			}
		}

		#endregion
	}
}
