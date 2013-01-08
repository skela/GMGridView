using System;
using System.Drawing;

using MonoTouch.UIKit;
using MonoTouch.Foundation;
using System.Diagnostics;

namespace GridView
{
	public class GMGridViewLayoutStrategyFactory
	{

		public static GMGridViewLayoutStrategy StrategyFromType(GMGridViewLayoutStrategyType type)
		{
			GMGridViewLayoutStrategy strategy = null;
			
			switch (type) 
			{
				case GMGridViewLayoutStrategyType.Vertical:
					strategy = new GMGridViewLayoutVerticalStrategy();
					break;
				case GMGridViewLayoutStrategyType.Horizontal:
					strategy = new GMGridViewLayoutHorizontalStrategy();
					break;
				case GMGridViewLayoutStrategyType.HorizontalPagedLTR:
					strategy = new GMGridViewLayoutHorizontalPagedLTRStrategy();
					break;
				case GMGridViewLayoutStrategyType.HorizontalPagedTTB:
					strategy = new GMGridViewLayoutHorizontalPagedTTBStrategy();
					break;
			}

			return strategy;
		}
	}
	
	public enum GMGridViewLayoutStrategyType
	{
		Vertical,
		Horizontal,
		HorizontalPagedLTR,
		HorizontalPagedTTB
	}

	public interface GMGridViewLayoutStrategy
	{
		bool RequiresEnablingPaging();
		GMGridViewLayoutStrategyType getType();
		void setType(GMGridViewLayoutStrategyType type);

		// Setup
		void setupItemSize(SizeF itemSize,int itemSpacing,UIEdgeInsets minEdgeInsets,bool isGridCentered);
		
		// Recomputing
		void rebaseWithItemCount(int itemCount,RectangleF insideOfBounds);
		
		// Fetching the results
		SizeF getContentSize();
		PointF originForItemAtPosition(int position);
		int itemPositionFromLocation(PointF location);
		NSRange rangeOfPositionsInBoundsFromOffset(PointF offset);
	}

	public class GMGridViewLayoutStrategyBase
	{
		// Constants
		public int GMGV_INVALID_POSITION = GMGridViewConstants.GMGV_INVALID_POSITION;

		// All of these vars should be set in the init method
		protected GMGridViewLayoutStrategyType type;
		
		// All of these vars should be set in the setup method of the child class
		protected SizeF itemSize;
		protected int itemSpacing;
		protected UIEdgeInsets minEdgeInsets;
		protected bool centeredGrid;
		
		// All of these vars should be set in the rebase method of the child class
		protected int itemCount;
		protected UIEdgeInsets edgeInsets;
		protected RectangleF gridBounds;
		protected SizeF contentSize;

		public GMGridViewLayoutStrategyBase ()
		{
		}

		public void setupItemSize(SizeF itemSize,int itemSpacing,UIEdgeInsets minEdgeInsets,bool isGridCentered)
		{
			this.itemSize      = itemSize;
			this.itemSpacing   = itemSpacing;
			this.minEdgeInsets = minEdgeInsets;
			this.centeredGrid  = isGridCentered;
		}

		public void setEdgeAndContentSizeFromAbsoluteContentSize(SizeF actualContentSize)
		{
			if (centeredGrid)
			{
				int widthSpace, heightSpace;        
				int top, left, bottom, right;

				widthSpace  = (int)Math.Floor((gridBounds.Size.Width  - actualContentSize.Width)  / 2.0f);
				heightSpace = (int)Math.Floor((gridBounds.Size.Height - actualContentSize.Height) / 2.0f);

				left   = (int)Math.Max(widthSpace,  minEdgeInsets.Left);
				right  = (int)Math.Max(widthSpace,  minEdgeInsets.Right);
				top    = (int)Math.Max(heightSpace, minEdgeInsets.Top);
				bottom = (int)Math.Max(heightSpace, minEdgeInsets.Bottom);

				edgeInsets = new UIEdgeInsets(top, left, bottom, right);
			}
			else
			{
				edgeInsets = minEdgeInsets;
			}

			contentSize = new SizeF(actualContentSize.Width+edgeInsets.Left+edgeInsets.Right,actualContentSize.Height+edgeInsets.Top+edgeInsets.Bottom);
		}

		public GMGridViewLayoutStrategyType getType()
		{
			return type;
		}

		public void setType(GMGridViewLayoutStrategyType type)
		{
			this.type = type;
		}

		public SizeF getContentSize()
		{
			return contentSize;
		}
	}

	public class GMGridViewLayoutVerticalStrategy : GMGridViewLayoutStrategyBase,GMGridViewLayoutStrategy
	{
		int  numberOfItemsPerRow;

		public bool RequiresEnablingPaging()
		{
			return false;
		}

		public GMGridViewLayoutVerticalStrategy()
		{
			setType(GMGridViewLayoutStrategyType.Vertical);
		}

		public void rebaseWithItemCount(int count,RectangleF insideOfBounds)
		{
			itemCount  = count;
			gridBounds = insideOfBounds;
			
			RectangleF actualBounds = new RectangleF(0,
			                                         0, 
			                                 insideOfBounds.Size.Width  - minEdgeInsets.Right - minEdgeInsets.Left, 
			                                 insideOfBounds.Size.Height - minEdgeInsets.Top   - minEdgeInsets.Bottom);

			numberOfItemsPerRow = 1;
			
			while ((numberOfItemsPerRow + 1) * (itemSize.Width + itemSpacing) - itemSpacing <= actualBounds.Size.Width)
			{
				numberOfItemsPerRow++;
			}
			
			int numberOfRows = (int)Math.Ceiling(itemCount / (1.0 * numberOfItemsPerRow));
			
			SizeF actualContentSize = new SizeF((float)Math.Ceiling(Math.Min(itemCount, numberOfItemsPerRow) * (itemSize.Width + itemSpacing)) - itemSpacing, 
			                                    (float)Math.Ceiling(numberOfRows * (itemSize.Height + itemSpacing)) - itemSpacing);

			setEdgeAndContentSizeFromAbsoluteContentSize(actualContentSize);
		}

		public PointF originForItemAtPosition(int position)		
		{
			PointF origin = new PointF();
			
			if (numberOfItemsPerRow > 0 && position >= 0) 
			{
				uint col = (uint) ( position % numberOfItemsPerRow );
				uint row = (uint) ( position / numberOfItemsPerRow );
				
				origin = new PointF(col * (itemSize.Width + itemSpacing) + edgeInsets.Left,
				                     row * (itemSize.Height + itemSpacing) + edgeInsets.Top);
			}

			return origin;
		}

		public int itemPositionFromLocation(PointF location)
		{

			PointF relativeLocation = new PointF(location.X - edgeInsets.Left,
			                                       location.Y - edgeInsets.Top);
			
			int col = (int) (relativeLocation.X / (itemSize.Width + itemSpacing)); 
			int row = (int) (relativeLocation.Y / (itemSize.Height + itemSpacing));
			
			int position = col + row * numberOfItemsPerRow;
			
			if (position >= itemCount || position < 0) 
			{
				position = GMGV_INVALID_POSITION;
			}
			else
			{
				PointF itemOrigin = originForItemAtPosition(position);
				RectangleF itemFrame = new RectangleF(itemOrigin.X, 
				                              itemOrigin.Y, 
				                              itemSize.Width, 
				                              itemSize.Height);

				if (!itemFrame.Contains(location)) 
				{
					position = GMGV_INVALID_POSITION;
				}
			}

			return position;
		}
		
		public NSRange rangeOfPositionsInBoundsFromOffset(PointF offset)
		{
			PointF contentOffset = new PointF(Math.Max(0, offset.X), 
			                                  Math.Max(0, offset.Y));
			
			float itemHeight = itemSize.Height + itemSpacing;
			
			int firstRow = (int) Math.Max(0, (int)(contentOffset.Y / itemHeight) - 1);
			
			int lastRow = (int) Math.Ceiling((contentOffset.Y + gridBounds.Size.Height) / itemHeight);
			
			int firstPosition = firstRow * numberOfItemsPerRow;
			int lastPosition  = ((lastRow + 1) * numberOfItemsPerRow);

			return new NSRange(firstPosition, (lastPosition - firstPosition));
		}
	}

	public class GMGridViewLayoutHorizontalStrategy : GMGridViewLayoutStrategyBase,GMGridViewLayoutStrategy
	{
		protected int numberOfItemsPerColumn;

		public bool RequiresEnablingPaging()
		{
			return false;
		}

		public GMGridViewLayoutHorizontalStrategy()		
		{
			setType(GMGridViewLayoutStrategyType.Horizontal);
		}
		
		public virtual void rebaseWithItemCount(int count,RectangleF insideOfBounds)
		{
			itemCount  = count;
			gridBounds = insideOfBounds;
			
			RectangleF actualBounds = new RectangleF(0, 
			                                 0, 
			                                 insideOfBounds.Size.Width  - minEdgeInsets.Right - minEdgeInsets.Left, 
			                                 insideOfBounds.Size.Height - minEdgeInsets.Top   - minEdgeInsets.Bottom);
			
			numberOfItemsPerColumn = 1;
			
			while ((numberOfItemsPerColumn + 1) * (itemSize.Height + itemSpacing) - itemSpacing <= actualBounds.Size.Height)
			{
				numberOfItemsPerColumn++;
			}
			
			int numberOfColumns = (int) Math.Ceiling(itemCount / (1.0 * numberOfItemsPerColumn));
			
			SizeF actualContentSize = new SizeF((float)Math.Ceiling(numberOfColumns * (itemSize.Width + itemSpacing)) - itemSpacing, 
			                                      (float)Math.Ceiling(Math.Min(itemCount, numberOfItemsPerColumn) * (itemSize.Height + itemSpacing)) - itemSpacing);
			
			setEdgeAndContentSizeFromAbsoluteContentSize(actualContentSize);
		}
		
		public virtual PointF originForItemAtPosition(int position)
		{
			PointF origin = new PointF();
			
			if (numberOfItemsPerColumn > 0 && position >= 0) 
			{
				uint col = (uint) (position / numberOfItemsPerColumn);
				uint row = (uint) (position % numberOfItemsPerColumn);
				
				origin = new PointF(col * (itemSize.Width + itemSpacing) + edgeInsets.Left,
				                     row * (itemSize.Height + itemSpacing) + edgeInsets.Top);
			}
			
			return origin;
		}
		
		public virtual int itemPositionFromLocation(PointF location)
		{
			PointF relativeLocation = new PointF(location.X - edgeInsets.Left,
			                                       location.Y - edgeInsets.Top);
			
			int col = (int) (relativeLocation.X / (itemSize.Width + itemSpacing)); 
			int row = (int) (relativeLocation.Y / (itemSize.Height + itemSpacing));
			
			int position = row + col * numberOfItemsPerColumn;
			
			if (position >= itemCount || position < 0) 
			{
				position = GMGV_INVALID_POSITION;
			}
			else
			{
				PointF itemOrigin = originForItemAtPosition(position);
				RectangleF itemFrame = new RectangleF(itemOrigin.X, 
				                              itemOrigin.Y, 
				                              itemSize.Width, 
				                              itemSize.Height);

				if (!itemFrame.Contains(location)) 
				{
					position = GMGV_INVALID_POSITION;
				}
			}
			
			return position;
		}
		
		public virtual NSRange rangeOfPositionsInBoundsFromOffset(PointF offset)
		{
			PointF contentOffset = new PointF(Math.Max(0, offset.X), 
			                                  Math.Max(0, offset.Y));
			
			float itemWidth = itemSize.Width + itemSpacing;
			
			int firstCol = (int) Math.Max(0, (int)(contentOffset.X / itemWidth) - 1);
			
			int lastCol = (int)Math.Ceiling((contentOffset.X + gridBounds.Size.Width) / itemWidth);
			
			int firstPosition = firstCol * numberOfItemsPerColumn;
			int lastPosition  = ((lastCol + 1) * numberOfItemsPerColumn);
			
			return new NSRange(firstPosition, (lastPosition - firstPosition));
		}
	}

	public class GMGridViewLayoutHorizontalPagedStrategy : GMGridViewLayoutHorizontalStrategy
	{
		protected int numberOfItemsPerRow;
		protected int numberOfItemsPerPage;
		protected int numberOfPages;

		public bool RequiresEnablingPaging()
		{
			return true;
		}

		public override void rebaseWithItemCount(int count,RectangleF insideOfBounds)
		{
			base.rebaseWithItemCount(count,insideOfBounds);
						
			numberOfItemsPerRow = 1;
			
			int gridContentMaxWidth = (int) ( gridBounds.Size.Width - minEdgeInsets.Right - minEdgeInsets.Left );
			
			while ((numberOfItemsPerRow + 1) * (itemSize.Width + itemSpacing) - itemSpacing <= gridContentMaxWidth)
			{
				numberOfItemsPerRow++;
			}
			
			numberOfItemsPerPage = numberOfItemsPerRow * numberOfItemsPerColumn;
			numberOfPages = (int)Math.Ceiling(itemCount * 1.0 / numberOfItemsPerPage);
			
			SizeF onePageSize = new SizeF(numberOfItemsPerRow * (itemSize.Width + itemSpacing) - itemSpacing, 
			                                numberOfItemsPerColumn * (itemSize.Height + itemSpacing) - itemSpacing);
			
			if (centeredGrid)
			{
				int widthSpace, heightSpace;        
				int top, left, bottom, right;
				
				widthSpace  = (int) Math.Floor((gridBounds.Size.Width  - onePageSize.Width)  / 2.0);
				heightSpace = (int) Math.Floor((gridBounds.Size.Height - onePageSize.Height) / 2.0);
				
				left   = (int) Math.Max(widthSpace,  minEdgeInsets.Left);
				right  = (int) Math.Max(widthSpace,  minEdgeInsets.Right);
				top    = (int) Math.Max(heightSpace, minEdgeInsets.Top);
				bottom = (int) Math.Max(heightSpace, minEdgeInsets.Bottom);
				
				edgeInsets = new UIEdgeInsets(top, left, bottom, right);
			}
			else
			{
				edgeInsets = minEdgeInsets;
			}
			
			contentSize = new SizeF(insideOfBounds.Size.Width * numberOfPages, 
			                        insideOfBounds.Size.Height);
		}

		public int pageForItemAtIndex(int index)
		{    
			return (int)Math.Max(0, Math.Floor(index * 1.0 / numberOfItemsPerPage * 1.0));
		}

		public PointF originForItemAtColumn(int column,int row,int page)
		{
			PointF offset = new PointF(page * gridBounds.Size.Width, 
			                             0);
			
			float x = column * (itemSize.Width + itemSpacing) + edgeInsets.Left;
			float y = row * (itemSize.Height + itemSpacing) + edgeInsets.Top;
			
			return new PointF(x + offset.X, 
			                   y + offset.Y);
		}

		public virtual int positionForItemAtColumn(int column,int row,int page)
		{
			return column + row * numberOfItemsPerRow + (page * numberOfItemsPerPage); 
		}

		public virtual int columnForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return position % numberOfItemsPerRow;;
		}

		public virtual int rowForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return (int)Math.Floor((double) position / (double)numberOfItemsPerRow);
		}

		public override PointF originForItemAtPosition(int position)
		{
			int page = pageForItemAtIndex(position);
			
			position %= numberOfItemsPerPage;
			
			int row = rowForItemAtPosition(position);
			int column = columnForItemAtPosition(position);
			
			PointF origin = originForItemAtColumn(column,row,page);
			
			return origin;
		}

		public override int itemPositionFromLocation(PointF location)
		{
			float fpage = 0;
			while ((fpage + 1) * gridBounds.Size.Width < location.X) 
			{
				fpage++;
			}

			int page = (int)fpage;

			PointF originForFirstItemInPage = originForItemAtColumn(0,0,page);
			
			PointF relativeLocation = new PointF(location.X - originForFirstItemInPage.X,
			                                       location.Y - originForFirstItemInPage.Y);
			
			int col = (int) (relativeLocation.X / (itemSize.Width + itemSpacing)); 
			int row = (int) (relativeLocation.Y / (itemSize.Height + itemSpacing));
			
			int position = positionForItemAtColumn(col,row,page);
			
			if (position >= itemCount || position < 0) 
			{
				position = GMGV_INVALID_POSITION;
			}
			else
			{
				PointF itemOrigin = originForItemAtPosition(position);
				RectangleF itemFrame = new RectangleF(itemOrigin.X, 
				                              itemOrigin.Y, 
				                              itemSize.Width, 
				                              itemSize.Height);

				if (!itemFrame.Contains(location)) 
				{
					position = GMGV_INVALID_POSITION;
				}
			}
			
			return position;
		}

		public override NSRange rangeOfPositionsInBoundsFromOffset(PointF offset)
		{
			PointF contentOffset = new PointF(Math.Max(0, offset.X), 
			                                  Math.Max(0, offset.Y));
			
			int page = (int) Math.Floor(contentOffset.X / gridBounds.Size.Width);
			
			int firstPosition = Math.Max(0, (page - 1) * numberOfItemsPerPage);
			int lastPosition  = Math.Min(firstPosition + 3 * numberOfItemsPerPage, itemCount);
			
			return new NSRange(firstPosition, (lastPosition - firstPosition));
		}
	}

	public class GMGridViewLayoutHorizontalPagedLTRStrategy : GMGridViewLayoutHorizontalPagedStrategy
	{
		public GMGridViewLayoutHorizontalPagedLTRStrategy() : base()
		{
			setType(GMGridViewLayoutStrategyType.HorizontalPagedLTR);
		}
	}

	public class GMGridViewLayoutHorizontalPagedTTBStrategy : GMGridViewLayoutHorizontalPagedStrategy
	{
		public GMGridViewLayoutHorizontalPagedTTBStrategy() : base()
		{
			setType(GMGridViewLayoutStrategyType.HorizontalPagedTTB);
		}

		public override int positionForItemAtColumn(int column,int row,int page)
		{
			return row + column * numberOfItemsPerColumn + (page * numberOfItemsPerPage); 
		}
		
		public override int columnForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return (int) Math.Floor( (double)position / (double)numberOfItemsPerColumn);
		}
		
		public override int rowForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return position % numberOfItemsPerColumn;
		}
	}


}

