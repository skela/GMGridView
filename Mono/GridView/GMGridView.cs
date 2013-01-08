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

namespace GridView
{
	public enum GMGridViewStyle
	{
		Push=0,
		Swap
	}
	
	public enum GMGridViewScrollPosition
	{
		None,
		Top,
		Middle,
		Bottom
	}
	
	public enum GMGridViewItemAnimation
	{
		None=0,
		Fade,
		Scroll= 1<<7
	}

	#region GMGridViewDataSource
	
	public interface GMGridViewDataSource
	{			
		//@required
		int numberOfItemsInGMGridView(GMGridView gridView);
		SizeF gridViewSizeForItemsInInterfaceOrientation(GMGridView gridView,UIInterfaceOrientation orientation);
		GMGridViewCell gridViewCellForItemAtIndex(GMGridView gridView,int index);
		//@optional
		bool gridViewCanDeleteItemAtIndex(GMGridView gridView,int index); // Allow a cell to be deletable. If not implemented, YES is assumed.
	}
	
	#endregion
	
	#region GMGridViewActionDelegate
	
	public interface GMGridViewActionDelegate
	{
		//@required
		void gridViewDidTapOnItemAtIndex(GMGridView gridView,int position);
		
		//@optional
		// Tap on space without any items
		void gridViewDidTapOnEmptySpace(GMGridView gridView);
		// Called when the delete-button has been pressed. Required to enable editing mode.
		// This method wont delete the cell automatically. Call the delete method of the gridView when appropriate.
		void gridViewProcessDeleteActionForItemAtIndex(GMGridView gridView,int index);
		void gridViewChangeEdit(GMGridView gridView,bool edit);
	}
	
	#endregion
	
	#region GMGridViewSortingDelegate
	
	public interface GMGridViewSortingDelegate
	{
		//@required
		// Item moved - right place to update the data structure
		void gridViewMoveItemAtIndex(GMGridView gridView,int oldIndex,int newIndex);
		void gridViewExchangeItemAtIndex(GMGridView gridView,int index1,int index2);		
		
		//@optional
		// Sorting started/ended - indexes are not specified on purpose (not the right place to update data structure)
		void gridViewDidStartMovingCell(GMGridView gridView,GMGridViewCell cell);
		void gridViewDidEndMovingCell(GMGridView gridView,GMGridViewCell cell);
		// Enable/Disable the shaking behavior of an item being moved
		bool gridViewShouldAllowShakingBehaviorWhenMovingCell(GMGridView gridView,GMGridViewCell view,int index);
	}
	
	#endregion
	
	#region GMGridViewTransformationDelegate
	
	public interface GMGridViewTransformationDelegate
	{
		//@required
		// Fullsize
		SizeF gridViewSizeInFullSizeForCell(GMGridView gridView,GMGridViewCell cell,int index,UIInterfaceOrientation orientation);
		UIView gridViewFullSizeViewForCell(GMGridView gridView,GMGridViewCell cell,int index);

		// Transformation (pinch, drag, rotate) of the item
		//@optional
		void gridViewDidStartTransformingCell(GMGridView gridView,GMGridViewCell cell);
		void gridViewDidEnterFullSizeForCell(GMGridView gridView,GMGridViewCell cell);
		void gridViewDidEndTransformingCell(GMGridView gridView,GMGridViewCell cell);
	}
	
	#endregion

	public class GMGridView : UIScrollView
	{
		// Constants
		public int GMGV_INVALID_POSITION = GMGridViewConstants.GMGV_INVALID_POSITION;
		public const int kTagOffset = 50;
		public const float kDefaultAnimationDuration = 0.3f;
		public const UIViewAnimationOptions kDefaultAnimationOptions = UIViewAnimationOptions.BeginFromCurrentState | UIViewAnimationOptions.AllowUserInteraction;

		public GMGridView(IntPtr handle) : base(handle)
		{
			CommonInit ();
		}
		
		[Export("initWithCoder:")]
		public GMGridView (NSCoder coder) : base(coder)
		{
			CommonInit ();
		}

		[Export("initWithFrame:")]
		public GMGridView (RectangleF rect) : base(rect)
		{
			CommonInit ();
		}

		GMGridViewLayoutStrategy layoutStrategy;
		UIView mainSuperView;
		bool editing;
		int itemSpacing;
		GMGridViewStyle style;
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
		GMGridViewCell transformingItem;
		bool inFullSizeMode;
		bool inTransformingState;
		bool itemsSubviewsCacheIsValid;
		int firstPositionLoaded;
		int lastPositionLoaded;
		int numberTotalItems;
		GMGridViewCell sortMovingItem;

		GMGridViewDataSource dataSource;  					// Required
		GMGridViewActionDelegate actionDelegate;            // Optional - to get taps callback & deleting item
		GMGridViewSortingDelegate sortingDelegate;          // Optional - to enable sorting
		GMGridViewTransformationDelegate transformDelegate; // Optional - to enable fullsize mode

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
			layoutStrategy = GMGridViewLayoutStrategyFactory.StrategyFromType(GMGridViewLayoutStrategyType.Vertical);				
			
			mainSuperView = this;
			editing = false;
			itemSpacing = 10;
			style = GMGridViewStyle.Swap;
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
			return (loc - range.Location < range.Length);
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

		public void LayoutSubviewsWithAnimation(GMGridViewItemAnimation animation)
		{
			RecomputeSizeAnimated(animation!=GMGridViewItemAnimation.None);
				//!(animation & GMGridViewItemAnimation.None));
			RelayoutItemsAnimated(animation==GMGridViewItemAnimation.Fade);  // only supported animation for now
			LoadRequiredItems();
		}

		public override void LayoutSubviews()
		{
			base.LayoutSubviews();
			
			if (rotationActive) 
			{
				rotationActive = false;
				
				// Updating all the items size
				SizeF newItemSize = dataSource.gridViewSizeForItemsInInterfaceOrientation(this,UIApplication.SharedApplication.StatusBarOrientation);

				if (!newItemSize.Equals(itemSize)) 
				{
					itemSize = newItemSize;

					ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell cell,out bool stop)
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
					SizeF fullSize = transformDelegate.gridViewSizeInFullSizeForCell(this,transformingItem,position,UIApplication.SharedApplication.StatusBarOrientation);						

					if (!fullSize.Equals(transformingItem.fullSize)) 
					{
						PointF center = transformingItem.fullSizeView.Center;
						transformingItem.fullSize = fullSize;
						transformingItem.fullSizeView.Center = center;
					}
				}
				
				// Adding alpha animation to make the relayouting more smooth
				
				CATransition transition = CATransition.CreateAnimation();					
				transition.Duration = 0.25f;
				transition.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
				transition.Type = CATransition.TransitionFade;
				Layer.AddAnimation(transition,"rotationAnimation");

				ApplyWithoutAnimation(delegate{LayoutSubviewsWithAnimation(GMGridViewItemAnimation.None);});

				// Fixing the contentOffset when pagging enabled
				
				if (PagingEnabled) 
				{
					SetContentOffset(RectForPoint(ContentOffset,true).Location,true);
				}
			}
			else 
			{
				LayoutSubviewsWithAnimation(GMGridViewItemAnimation.None);
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

		public GMGridViewStyle Style
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

		public GMGridViewDataSource DataSource
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

		public GMGridViewTransformationDelegate TransformDelegate
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

		public GMGridViewActionDelegate ActionDelegate
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

		public GMGridViewSortingDelegate SortingDelegate
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
		
		public void SetLayoutStrategy(GMGridViewLayoutStrategy newLayoutStrategy)
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
				actionDelegate.gridViewChangeEdit(this,shouldEdit);
			}
		}
		
		public void SetEditing(bool shouldEdit,bool animated)
		{
			if (actionDelegate!=null && !IsInTransformingState && ((editing && !shouldEdit) || (!editing && shouldEdit)))
			{
				ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell cell,out bool stop)
				{
					stop=false;
					int index = PositionForItemSubview(cell);
					if (index != GMGV_INVALID_POSITION)
					{
						bool allowEdit = shouldEdit && dataSource.gridViewCanDeleteItemAtIndex(this,index);	
						cell.setEditing(allowEdit,animated);
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
					int position = layoutStrategy.itemPositionFromLocation(locationTouch);
					
					valid = (position == GMGV_INVALID_POSITION);
				}
				else 
				{
					valid = !isScrolling && !editing && !longPressGesture.hasRecognizedValidGesture();
				}
			}
			else if (gestureRecognizer == longPressGesture)
			{
				valid = (sortingDelegate!=null || enableEditOnLongPress) && !isScrolling && !editing;
			}
			else if (gestureRecognizer == sortingPanGesture) 
			{
				valid = (sortMovingItem != null && longPressGesture.hasRecognizedValidGesture());
			}
			else if(gestureRecognizer == rotationGesture || gestureRecognizer == pinchGesture || gestureRecognizer == panGesture)
			{
				if (transformDelegate != null && gestureRecognizer.NumberOfTouches == 2) 
				{
					PointF locationTouch1 = gestureRecognizer.LocationOfTouch(0,this);
					PointF locationTouch2 = gestureRecognizer.LocationOfTouch(1,this);
					
					int positionTouch1 = layoutStrategy.itemPositionFromLocation(locationTouch1);
	                int positionTouch2 = layoutStrategy.itemPositionFromLocation(locationTouch2);
					
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
			GMGridView gridView;

			public GridGestureRecognizer(GMGridView gridView) : base()
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
				int position = layoutStrategy.itemPositionFromLocation(locationTouch);
				
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
						
						int position = layoutStrategy.itemPositionFromLocation(location);
						
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
		void SortingAutoScrollMovementCheck()
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
			int position = layoutStrategy.itemPositionFromLocation(point);
			
			GMGridViewCell item = CellForItemAtIndex(position);
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
				sortingDelegate.gridViewDidStartMovingCell(this,sortMovingItem);
			}
			
			if (sortingDelegate!=null)
			{
				sortMovingItem.shake(sortingDelegate.gridViewShouldAllowShakingBehaviorWhenMovingCell(this,sortMovingItem,position));			
			}
			else
			{
				sortMovingItem.shake(true);				
			}
		}
		
		void SortingMoveDidStopAtPoint(PointF point)
		{
			sortMovingItem.shake(false);
			sortMovingItem.Tag = sortFuturePosition + kTagOffset;
			 
			RectangleF frameInScroll = mainSuperView.ConvertRectToView(sortMovingItem.Frame,this);

			sortMovingItem.RemoveFromSuperview();
			sortMovingItem.Frame=frameInScroll;
			AddSubview(sortMovingItem);

			PointF newOrigin = layoutStrategy.originForItemAtPosition(sortFuturePosition);
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
					sortingDelegate.gridViewDidEndMovingCell(this,sortMovingItem);
				}
				
				sortMovingItem = null;
				sortFuturePosition = GMGV_INVALID_POSITION;

				SetSubviewsCacheAsInvalid();
			});
		}
		
		void SortingMoveDidContinueToPoint(PointF point)
		{
			int position = layoutStrategy.itemPositionFromLocation(point);
			int tag = position + kTagOffset;
			
			if (position != GMGV_INVALID_POSITION && position != sortFuturePosition && position < numberTotalItems) 
			{
				bool positionTaken = false;
				
				ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell v,out bool stop)
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
						case GMGridViewStyle.Push:
						{
							if (position > sortFuturePosition) 
							{
								ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell v,out bool stop)
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
								ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell v,out bool stop)
								{
									stop=false;
									if ((v.Tag == tag || (v.Tag > tag && v.Tag <= sortFuturePosition + kTagOffset)) && v != sortMovingItem) 
									{
										v.Tag = v.Tag + 1;
										SendSubviewToBack(v);
									}
								});
							}

							sortingDelegate.gridViewMoveItemAtIndex(this,sortFuturePosition,position);
							RelayoutItemsAnimated(true);
							break;
						}
						case GMGridViewStyle.Swap:
						default:
						{
							if (sortMovingItem!=null) 
							{
								UIView v = CellForItemAtIndex(position);
								v.Tag = sortFuturePosition + kTagOffset;
								PointF origin = layoutStrategy.originForItemAtPosition(sortFuturePosition);

								UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
								delegate
				               	{
									v.Frame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
								},
								delegate
								{

								});
							}

							sortingDelegate.gridViewExchangeItemAtIndex(this,sortFuturePosition,position);

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
			int position = layoutStrategy.itemPositionFromLocation(locationTouch);
			
			if (position != GMGV_INVALID_POSITION) 
			{
				if (!editing) 
				{
					var cell = CellForItemAtIndex(position);
					if (cell!=null)
					{
						cell.IsHighlighted = false;
						if (actionDelegate!=null)
							actionDelegate.gridViewDidTapOnItemAtIndex(this,position);
					}
				}
			}
			else
			{ 
				if (actionDelegate!=null)
				{
					actionDelegate.gridViewDidTapOnEmptySpace(this);
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

		private GMGridViewCell NewItemSubViewForPosition(int position)
		{
			GMGridViewCell cell = dataSource.gridViewCellForItemAtIndex(this,position);				
			PointF origin = layoutStrategy.originForItemAtPosition(position);
			RectangleF frame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
			
			// To make sure the frame is not animated
			ApplyWithoutAnimation(delegate
			{
				cell.Frame = frame;
				cell.ContentView.Frame = cell.Bounds;
			});

			cell.Tag = position + kTagOffset;
			bool canEdit = editing && dataSource.gridViewCanDeleteItemAtIndex(this,position);
			cell.setEditing(canEdit,animated:false);
			
			GMGridView weakSelf = this;
			cell.DeleteBlock = delegate(GMGridViewCell aCell)
			{
				int index = weakSelf.PositionForItemSubview(aCell);
				if (index != GMGV_INVALID_POSITION) 
				{
					bool canDelete = weakSelf.dataSource.gridViewCanDeleteItemAtIndex(weakSelf,index);						
					if (canDelete)
					{
						weakSelf.actionDelegate.gridViewProcessDeleteActionForItemAtIndex(weakSelf,index);
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
							if (v is GMGridViewCell)
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

		private GMGridViewCell CellForItemAtIndex(int position)
		{
			GMGridViewCell view = null;

			ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell v,out bool stop)
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

		private int PositionForItemSubview(GMGridViewCell view)
		{
			return view.Tag >= kTagOffset ? view.Tag - kTagOffset : GMGV_INVALID_POSITION;
		}

		private void RecomputeSizeAnimated(bool animated)
		{
			layoutStrategy.setupItemSize(itemSize,itemSpacing,minEdgeInsets,centerGrid);
			layoutStrategy.rebaseWithItemCount(numberTotalItems,Bounds);

			SizeF contentSize = layoutStrategy.getContentSize();

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
				ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell view,out bool stop)
				{
					stop=false;
					if (view != sortMovingItem && view != transformingItem) 
					{
						int index = view.Tag - kTagOffset;
						PointF origin = layoutStrategy.originForItemAtPosition(index);							
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
					float currentScale = val.FloatValue;											
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
							transformingItem.stepToFullsizeWithAlpha(alpha);
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
					

				}break;
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
				
				PointF center = transformingItem.fullSizeView.Center;
				
				transformingItem.switchToFullSizeMode(false);					
				CGAffineTransform newTransform = CGAffineTransform.MakeScale(2.5f, 2.5f);
				transformingItem.ContentView.Transform = newTransform;
				transformingItem.ContentView.Center = center;
			}
			else if (transformingItem==null) 
			{        
				PointF locationTouch = gesture.LocationOfTouch(0,this);
				int positionTouch = layoutStrategy.itemPositionFromLocation(locationTouch);
				transformingItem = CellForItemAtIndex(positionTouch);
				
				RectangleF frameInMainView = ConvertRectToView(transformingItem.Frame,mainSuperView);

				transformingItem.RemoveFromSuperview();
				transformingItem.Frame = mainSuperView.Bounds;
				transformingItem.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
				transformingItem.ContentView.Frame = frameInMainView;
				mainSuperView.AddSubview(transformingItem);
				mainSuperView.BringSubviewToFront(transformingItem);
								
				transformingItem.fullSize = transformDelegate.gridViewSizeInFullSizeForCell(this,transformingItem,positionTouch,UIApplication.SharedApplication.StatusBarOrientation);
				transformingItem.fullSizeView = transformDelegate.gridViewFullSizeViewForCell(this,transformingItem,positionTouch);



				transformDelegate.gridViewDidEndTransformingCell(this,transformingItem);
			}
		}

		private bool IsInTransformingState
		{
			get
			{
				return transformingItem != null;
			}
		}

		[Export("transformingGestureDidFinish")]
		public void TransformingGestureDidFinish()
		{
			if (IsInTransformingState) 
			{
				if (lastScale > 2 && !inTransformingState) 
				{            
					lastRotation = 0;
					lastScale = 1;

					BringSubviewToFront(transformingItem);

					float rotationValue = (float)Math.Atan2(transformingItem.ContentView.Transform.xy,transformingItem.ContentView.Transform.xx);
					
					transformingItem.ContentView.Transform = CGAffineTransform.MakeIdentity();
					transformingItem.switchToFullSizeMode(true);

					transformingItem.BackgroundColor = UIColor.DarkGray.ColorWithAlpha(0.9f);

					transformingItem.fullSizeView.Transform =  CGAffineTransform.MakeRotation(rotationValue);

					UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
					delegate
					{
						transformingItem.fullSizeView.Transform = CGAffineTransform.MakeIdentity();
					},
					delegate
					{

					});
				
					inTransformingState = true;
					inFullSizeMode = true;

					transformDelegate.gridViewDidEnterFullSizeForCell(this,transformingItem);

					// Transfer the gestures on the fullscreen to make is they are accessible (depends on self.mainSuperView)
					transformingItem.fullSizeView.AddGestureRecognizer(pinchGesture);
					transformingItem.fullSizeView.AddGestureRecognizer(rotationGesture);
					transformingItem.fullSizeView.AddGestureRecognizer(panGesture);
				}
				else if (!inTransformingState)
				{
					lastRotation = 0;
					lastScale = 1.0f;
					
					GMGridViewCell transformingView = transformingItem;
					transformingItem = null;
					
					int position = PositionForItemSubview(transformingView);						
					PointF origin = layoutStrategy.originForItemAtPosition(position);
					
					RectangleF finalFrameInScroll = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
					RectangleF finalFrameInSuperview = ConvertRectToView(finalFrameInScroll,mainSuperView);

					transformingView.switchToFullSizeMode(false);
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
												
						transformingView.fullSizeView = null;
						inFullSizeMode = false;

						transformDelegate.gridViewDidEndTransformingCell(this,transformingView);

						// Transfer the gestures back
						AddGestureRecognizer(pinchGesture);
						AddGestureRecognizer(rotationGesture);
						AddGestureRecognizer(panGesture);
					});
				}
			}
		}

		#endregion

		#region Loading / Destroying Items and Reusing Cells

		private void LoadRequiredItems()
		{
			NSRange rangeOfPositions = layoutStrategy.rangeOfPositionsInBoundsFromOffset(ContentOffset);
			NSRange loadedPositionsRange = new NSRange(firstPositionLoaded,lastPositionLoaded - firstPositionLoaded);

			//Console.WriteLine(@"Range of locs: "+rangeOfPositions.ToString());

			// calculate new position range
			firstPositionLoaded = firstPositionLoaded == GMGV_INVALID_POSITION ? rangeOfPositions.Location : Math.Min(firstPositionLoaded, (int)rangeOfPositions.Location);
			lastPositionLoaded  = lastPositionLoaded == GMGV_INVALID_POSITION ? NSMaxRange(rangeOfPositions) : Math.Max(lastPositionLoaded, (int)(rangeOfPositions.Length + rangeOfPositions.Location));

			// remove now invisible items
			SetSubviewsCacheAsInvalid();
			CleanupUnseenItems();
			
			// add new cells
			bool forceLoad = firstPositionLoaded == GMGV_INVALID_POSITION || lastPositionLoaded == GMGV_INVALID_POSITION;
			int positionToLoad;
			for (int i = 0; i < rangeOfPositions.Length; i++) 
			{
				positionToLoad = i + rangeOfPositions.Location;

				//Console.WriteLine("NSLocationInRange:" + NSLocationInRange(positionToLoad, loadedPositionsRange).ToString());

				if ((forceLoad || !NSLocationInRange(positionToLoad, loadedPositionsRange)) && positionToLoad < numberTotalItems) 
				{
					//Console.WriteLine ("I'm here");
					if (CellForItemAtIndex(positionToLoad)==null) 
					{
						//Console.WriteLine ("Added grid cell at pos: " + positionToLoad.ToString());
						GMGridViewCell cell = NewItemSubViewForPosition(positionToLoad);							
						AddSubview(cell);
					}
				}
			}    
		}

		private void CleanupUnseenItems()
		{
			int cleanupCounter=0;

			NSRange rangeOfPositions = layoutStrategy.rangeOfPositionsInBoundsFromOffset(ContentOffset);
			GMGridViewCell cell;
			
			if ((int)rangeOfPositions.Location > firstPositionLoaded) 
			{
				for (int i = firstPositionLoaded; i < (int)rangeOfPositions.Location; i++) 
				{
					cell = CellForItemAtIndex(i);
					if(cell!=null)
					{
						QueueReusableCell(cell);
						cell.RemoveFromSuperview();
						cleanupCounter++;
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
						cleanupCounter++;
					}
				}

				Console.WriteLine ("Cleaned up " + cleanupCounter);

				lastPositionLoaded = NSMaxRange(rangeOfPositions);
				SetSubviewsCacheAsInvalid();
			}
		}

		private void QueueReusableCell(GMGridViewCell cell)
		{
			if (cell!=null) 
			{
				cell.prepareForReuse();
				cell.Alpha = 1;
				cell.BackgroundColor = UIColor.Clear;
				reusableCells.Add(cell);
			}
		}

		public GMGridViewCell DequeueReusableCell()
		{
			GMGridViewCell cell = (GMGridViewCell)reusableCells.AnyObject;
			
			if (cell!=null) 
			{
				reusableCells.Remove(cell);
			}
			
			return cell;
		}
		
		public GMGridViewCell DequeueReusableCellWithIdentifier(String identifier)
		{
			GMGridViewCell cell = null;

			foreach (GMGridViewCell reusableCell in reusableCells.ToArray<GMGridViewCell>())
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

			ItemSubviews.EnumerateGridCells(delegate(GMGridViewCell obj,out bool stop)
			{
				stop=false;
				if (obj is GMGridViewCell)				    
				{
					obj.RemoveFromSuperview();
					QueueReusableCell((GMGridViewCell)obj);
				}
			});
			
			firstPositionLoaded = GMGV_INVALID_POSITION;
			lastPositionLoaded  = GMGV_INVALID_POSITION;

			SetSubviewsCacheAsInvalid();
			
			int numberItems = dataSource.numberOfItemsInGMGridView(this);				
			itemSize = dataSource.gridViewSizeForItemsInInterfaceOrientation(this,UIApplication.SharedApplication.StatusBarOrientation);
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
			ReloadObjectAtIndex(index,animated ? GMGridViewItemAnimation.Scroll : GMGridViewItemAnimation.None);
		}
		
		public void ReloadObjectAtIndex(int index,GMGridViewItemAnimation animation)
		{
			Debug.Assert((index >= 0 && index < numberTotalItems), "Invalid index");
			
			UIView currentView = CellForItemAtIndex(index);
			
			GMGridViewCell cell = NewItemSubViewForPosition(index);
			PointF origin = layoutStrategy.originForItemAtPosition(index);
			cell.Frame = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
			cell.Alpha = 0;
			AddSubview(cell);
			
			currentView.Tag = kTagOffset - 1;
			bool shouldScroll = animation == GMGridViewItemAnimation.Scroll;
			bool animate = animation == GMGridViewItemAnimation.Fade;
			UIView.Animate(animate ? kDefaultAnimationDuration : 0.0,0.0,kDefaultAnimationOptions,
			delegate
			{
				if (shouldScroll) 
				{
					ScrollToObjectAtIndex(index,GMGridViewScrollPosition.None,false);
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
		
		public void ScrollToObjectAtIndex(int index,GMGridViewScrollPosition scrollPosition,bool animated)
		{
			index = Math.Max(0, index);
			index = Math.Min(index,numberTotalItems);
			
			PointF origin = layoutStrategy.originForItemAtPosition(index);
			RectangleF targetRect = RectForPoint(origin,PagingEnabled);
			
			if (!PagingEnabled)
			{
				RectangleF gridRect = new RectangleF(origin.X, origin.Y, itemSize.Width, itemSize.Height);
				
				switch (scrollPosition)
				{
					case GMGridViewScrollPosition.None:
					default:
						targetRect = gridRect; // no special coordinate handling
						break;
						
					case GMGridViewScrollPosition.Top:
						targetRect.Y = gridRect.Y;	// set target y origin to cell's y origin
						break;
						
					case GMGridViewScrollPosition.Middle:
						targetRect.Y = (float)Math.Max(gridRect.Y - (float)Math.Ceiling((targetRect.Size.Height - gridRect.Size.Height) * 0.5), 0.0);
						break;
						
					case GMGridViewScrollPosition.Bottom:
						targetRect.Y = (float)Math.Max((float)Math.Floor(gridRect.Y - (targetRect.Size.Height - gridRect.Size.Height)), 0.0);
						break;
				}
			}
			
			// Better performance animating ourselves instead of using animated:YES in scrollRectToVisible
			UIView.Animate(animated ? kDefaultAnimationDuration : 0,0,kDefaultAnimationOptions,
			delegate
			{
				ScrollRectToVisible(targetRect,false);
			},
			delegate
			{

			});
		}
		
		public void InsertObjectAtIndex(int index,bool animated)
		{
			InsertObjectAtIndex(index,animated ? GMGridViewItemAnimation.Scroll : GMGridViewItemAnimation.None);
		}
		
		public void InsertObjectAtIndex(int index,GMGridViewItemAnimation animation)
		{
			Debug.Assert((index >= 0 && index <= numberTotalItems), "Invalid index specified");
			
			GMGridViewCell cell = null;
			
			if (index >= firstPositionLoaded && index <= lastPositionLoaded) 
			{        
				cell = NewItemSubViewForPosition(index);
				
				for (int i = numberTotalItems - 1; i >= index; i--)
				{
					UIView oldView = CellForItemAtIndex(i);
					oldView.Tag = oldView.Tag + 1;
				}
				
				if (animation == GMGridViewItemAnimation.Fade) 
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
			RecomputeSizeAnimated(animation!=GMGridViewItemAnimation.None);
			
			bool shouldScroll = animation == GMGridViewItemAnimation.Scroll;
			if (shouldScroll)
			{
				UIView.Animate (kDefaultAnimationDuration,0,kDefaultAnimationOptions,
				delegate
				{
					ScrollToObjectAtIndex(index,GMGridViewScrollPosition.None,false);
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
			RemoveObjectAtIndex(index,GMGridViewItemAnimation.None);
		}

		public void RemoveObjectAtIndex(int index,GMGridViewItemAnimation animation)
		{
			Debug.Assert((index >= 0 && index < numberTotalItems), "Invalid index specified");
			
			GMGridViewCell cell = CellForItemAtIndex(index);
			
			for (int i = index + 1; i < numberTotalItems; i++)
			{
				GMGridViewCell oldView = CellForItemAtIndex(i);
				oldView.Tag = oldView.Tag - 1;
			}
			
			cell.Tag = kTagOffset - 1;
			numberTotalItems--;
			
			bool shouldScroll = animation == GMGridViewItemAnimation.Scroll;
			bool animate = animation == GMGridViewItemAnimation.Fade;

			UIView.Animate(animate ? kDefaultAnimationDuration : 0.0f,0,kDefaultAnimationOptions,
			delegate
			{
				cell.ContentView.Alpha = 0.3f;
				cell.Alpha = 0.0f;
				
				if (shouldScroll) 
				{
					ScrollToObjectAtIndex(index,GMGridViewScrollPosition.None,false);
				}
				RecomputeSizeAnimated((animation != GMGridViewItemAnimation.None));
			},
			delegate
			{
				cell.ContentView.Alpha = 1.0f;
				QueueReusableCell(cell);
				cell.RemoveFromSuperview();

				firstPositionLoaded = lastPositionLoaded = GMGV_INVALID_POSITION;
				LoadRequiredItems();
				RelayoutItemsAnimated(animate);
			});

			SetSubviewsCacheAsInvalid();
		}

		public void SwapObjectAtIndex(int index1,int index2,bool animated)
		{
			SwapObjectAtIndex(index1,index2,animated ? GMGridViewItemAnimation.Scroll : GMGridViewItemAnimation.None);
		}

		public void SwapObjectAtIndex(int index1,int index2,GMGridViewItemAnimation animation)
		{
			Debug.Assert((index1 >= 0 && index1 < numberTotalItems), "Invalid index1 specified");
			Debug.Assert((index2 >= 0 && index2 < numberTotalItems), "Invalid index2 specified");
			
			GMGridViewCell view1 = CellForItemAtIndex(index1);
			GMGridViewCell view2 = CellForItemAtIndex(index2);
			
			view1.Tag = index2 + kTagOffset;
			view2.Tag = index1 + kTagOffset;
			
			PointF view1Origin = layoutStrategy.originForItemAtPosition(index2);
			PointF view2Origin = layoutStrategy.originForItemAtPosition(index1);
			
			view1.Frame = new RectangleF(view1Origin.X, view1Origin.Y, itemSize.Width, itemSize.Height);
			view2.Frame = new RectangleF(view2Origin.X, view2Origin.Y, itemSize.Width, itemSize.Height);
			
			
			RectangleF visibleRect = new RectangleF(ContentOffset.X,
			                                        ContentOffset.Y, 
			                                		ContentSize.Width, 
			                                		ContentSize.Height);
			
			// Better performance animating ourselves instead of using animated:YES in scrollRectToVisible
			bool shouldScroll = animation == GMGridViewItemAnimation.Scroll;
			UIView.Animate(kDefaultAnimationDuration,0,kDefaultAnimationOptions,
			delegate
			{
				if (shouldScroll) 
				{
					if (!CGRectIntersectsRect(view2.Frame, visibleRect)) 
					{
						ScrollToObjectAtIndex(index1,GMGridViewScrollPosition.None,false);
					}
					else if (!CGRectIntersectsRect(view1.Frame, visibleRect)) 
					{
						ScrollToObjectAtIndex(index2,GMGridViewScrollPosition.None,false);
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
