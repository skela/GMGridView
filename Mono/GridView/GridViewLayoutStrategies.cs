using System;
using System.Drawing;

using MonoTouch.UIKit;
using MonoTouch.Foundation;
using System.Diagnostics;

namespace Grid
{
	public class GridViewLayoutStrategyFactory
	{

		public static GridViewLayoutStrategy StrategyFromType(GridViewLayoutStrategyType type)
		{
			GridViewLayoutStrategy strategy = null;
			
			switch (type) 
			{
				case GridViewLayoutStrategyType.Vertical:
					strategy = new GridViewLayoutVerticalStrategy();
					break;
				case GridViewLayoutStrategyType.Horizontal:
					strategy = new GridViewLayoutHorizontalStrategy();
					break;
				case GridViewLayoutStrategyType.HorizontalPagedLTR:
					strategy = new GridViewLayoutHorizontalPagedLTRStrategy();
					break;
				case GridViewLayoutStrategyType.HorizontalPagedTTB:
					strategy = new GridViewLayoutHorizontalPagedTTBStrategy();
					break;
			}

			return strategy;
		}
	}
	
	public enum GridViewLayoutStrategyType
	{
		Vertical,
		Horizontal,
		HorizontalPagedLTR,
		HorizontalPagedTTB
	}

	public interface GridViewLayoutStrategy
	{
		bool RequiresEnablingPaging();
		GridViewLayoutStrategyType GetGridLayoutStrategyType();
		void SetGridLayoutStrategyType(GridViewLayoutStrategyType type);

		// Setup
		void SetupItemSize(SizeF itemSize,int itemSpacing,UIEdgeInsets minEdgeInsets,bool isGridCentered);
		
		// Recomputing
		void RebaseWithItemCount(int itemCount,RectangleF insideOfBounds);
		
		// Fetching the results
		SizeF GetContentSize();
		PointF OriginForItemAtPosition(int position);
		int ItemPositionFromLocation(PointF location);
		NSRange RangeOfPositionsInBoundsFromOffset(PointF offset);
	}

	public class GridViewLayoutStrategyBase
	{
		// Constants
		public int GMGV_INVALID_POSITION = GridViewConstants.GMGV_INVALID_POSITION;

		// All of these vars should be set in the init method
		protected GridViewLayoutStrategyType type;
		
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

		public GridViewLayoutStrategyBase ()
		{
		}

		public void SetupItemSize(SizeF itemSize,int itemSpacing,UIEdgeInsets minEdgeInsets,bool isGridCentered)
		{
			this.itemSize      = itemSize;
			this.itemSpacing   = itemSpacing;
			this.minEdgeInsets = minEdgeInsets;
			this.centeredGrid  = isGridCentered;
		}

		public void SetEdgeAndContentSizeFromAbsoluteContentSize(SizeF actualContentSize)
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

		public GridViewLayoutStrategyType GetGridLayoutStrategyType()
		{
			return type;
		}

		public void SetGridLayoutStrategyType(GridViewLayoutStrategyType type)
		{
			this.type = type;
		}

		public SizeF GetContentSize()
		{
			return contentSize;
		}
	}

	public class GridViewLayoutVerticalStrategy : GridViewLayoutStrategyBase,GridViewLayoutStrategy
	{
		int  numberOfItemsPerRow;

		public virtual bool RequiresEnablingPaging()
		{
			return false;
		}

		public GridViewLayoutVerticalStrategy()
		{
			SetGridLayoutStrategyType(GridViewLayoutStrategyType.Vertical);
		}

		public void RebaseWithItemCount(int count,RectangleF insideOfBounds)
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

			SetEdgeAndContentSizeFromAbsoluteContentSize(actualContentSize);
		}

		public PointF OriginForItemAtPosition(int position)		
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

		public int ItemPositionFromLocation(PointF location)
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
				PointF itemOrigin = OriginForItemAtPosition(position);
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
		
		public NSRange RangeOfPositionsInBoundsFromOffset(PointF offset)
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

	public class GridViewLayoutHorizontalStrategy : GridViewLayoutStrategyBase,GridViewLayoutStrategy
	{
		protected int numberOfItemsPerColumn;

		public virtual bool RequiresEnablingPaging()
		{
			return false;
		}

		public GridViewLayoutHorizontalStrategy()		
		{
			SetGridLayoutStrategyType(GridViewLayoutStrategyType.Horizontal);
		}
		
		public virtual void RebaseWithItemCount(int count,RectangleF insideOfBounds)
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
			
			SetEdgeAndContentSizeFromAbsoluteContentSize(actualContentSize);
		}
		
		public virtual PointF OriginForItemAtPosition(int position)
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
		
		public virtual int ItemPositionFromLocation(PointF location)
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
				PointF itemOrigin = OriginForItemAtPosition(position);
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
		
		public virtual NSRange RangeOfPositionsInBoundsFromOffset(PointF offset)
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

	public class GridViewLayoutHorizontalPagedStrategy : GridViewLayoutHorizontalStrategy
	{
		protected int numberOfItemsPerRow;
		protected int numberOfItemsPerPage;
		protected int numberOfPages;

		public override bool RequiresEnablingPaging()
		{
			return true;
		}

		public override void RebaseWithItemCount(int count,RectangleF insideOfBounds)
		{
			base.RebaseWithItemCount(count,insideOfBounds);
						
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

		public int PageForItemAtIndex(int index)
		{    
			return (int)Math.Max(0, Math.Floor(index * 1.0 / numberOfItemsPerPage * 1.0));
		}

		public PointF OriginForItemAtColumn(int column,int row,int page)
		{
			PointF offset = new PointF(page * gridBounds.Size.Width, 
			                             0);
			
			float x = column * (itemSize.Width + itemSpacing) + edgeInsets.Left;
			float y = row * (itemSize.Height + itemSpacing) + edgeInsets.Top;
			
			return new PointF(x + offset.X, 
			                   y + offset.Y);
		}

		public virtual int PositionForItemAtColumn(int column,int row,int page)
		{
			return column + row * numberOfItemsPerRow + (page * numberOfItemsPerPage); 
		}

		public virtual int ColumnForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return position % numberOfItemsPerRow;;
		}

		public virtual int RowForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return (int)Math.Floor((double) position / (double)numberOfItemsPerRow);
		}

		public override PointF OriginForItemAtPosition(int position)
		{
			int page = PageForItemAtIndex(position);
			
			position %= numberOfItemsPerPage;
			
			int row = RowForItemAtPosition(position);
			int column = ColumnForItemAtPosition(position);
			
			PointF origin = OriginForItemAtColumn(column,row,page);
			
			return origin;
		}

		public override int ItemPositionFromLocation(PointF location)
		{
			float fpage = 0;
			while ((fpage + 1) * gridBounds.Size.Width < location.X) 
			{
				fpage++;
			}

			int page = (int)fpage;

			PointF originForFirstItemInPage = OriginForItemAtColumn(0,0,page);
			
			PointF relativeLocation = new PointF(location.X - originForFirstItemInPage.X,
			                                       location.Y - originForFirstItemInPage.Y);
			
			int col = (int) (relativeLocation.X / (itemSize.Width + itemSpacing)); 
			int row = (int) (relativeLocation.Y / (itemSize.Height + itemSpacing));
			
			int position = PositionForItemAtColumn(col,row,page);
			
			if (position >= itemCount || position < 0) 
			{
				position = GMGV_INVALID_POSITION;
			}
			else
			{
				PointF itemOrigin = OriginForItemAtPosition(position);
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

		public override NSRange RangeOfPositionsInBoundsFromOffset(PointF offset)
		{
			PointF contentOffset = new PointF(Math.Max(0, offset.X), 
			                                  Math.Max(0, offset.Y));
			
			int page = (int) Math.Floor(contentOffset.X / gridBounds.Size.Width);
			
			int firstPosition = Math.Max(0, (page - 1) * numberOfItemsPerPage);
			int lastPosition  = Math.Min(firstPosition + 3 * numberOfItemsPerPage, itemCount);
			
			return new NSRange(firstPosition, (lastPosition - firstPosition));
		}
	}

	public class GridViewLayoutHorizontalPagedLTRStrategy : GridViewLayoutHorizontalPagedStrategy
	{
		public GridViewLayoutHorizontalPagedLTRStrategy() : base()
		{
			SetGridLayoutStrategyType(GridViewLayoutStrategyType.HorizontalPagedLTR);
		}
	}

	public class GridViewLayoutHorizontalPagedTTBStrategy : GridViewLayoutHorizontalPagedStrategy
	{
		public GridViewLayoutHorizontalPagedTTBStrategy() : base()
		{
			SetGridLayoutStrategyType(GridViewLayoutStrategyType.HorizontalPagedTTB);
		}

		public override int PositionForItemAtColumn(int column,int row,int page)
		{
			return row + column * numberOfItemsPerColumn + (page * numberOfItemsPerPage); 
		}
		
		public override int ColumnForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return (int) Math.Floor( (double)position / (double)numberOfItemsPerColumn);
		}
		
		public override int RowForItemAtPosition(int position)
		{
			position %= numberOfItemsPerPage;
			return position % numberOfItemsPerColumn;
		}
	}


}

