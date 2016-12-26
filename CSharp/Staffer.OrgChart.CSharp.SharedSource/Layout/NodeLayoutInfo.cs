﻿using System;
using Staffer.OrgChart.Annotations;

namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// Supporting layout-related information, attached to every node of a visual tree.
    /// </summary>
    public class NodeLayoutInfo
    {
        /// <summary>
        /// Effective layout strategy, derived from settings or inherited from parent.
        /// </summary>
        public LayoutStrategyBase EffectiveLayoutStrategy
        {
            [NotNull]
            set { m_effectiveLayoutStrategy = value; }
        }

        /// <summary>
        /// Returns value of <see cref="EffectiveLayoutStrategy"/>, throws if <c>null</c>.
        /// </summary>
        [NotNull]
        public LayoutStrategyBase RequireLayoutStrategy()
        {
            if (m_effectiveLayoutStrategy == null)
            {
                throw new Exception(nameof(EffectiveLayoutStrategy) + " is not set");
            }

            return m_effectiveLayoutStrategy;
        }

        /// <summary>
        /// Number of visible regular children in this node's immediate children list
        /// that are affecting each other as siblings during layout.
        /// Some special auto-generated spacer boxes may not be included into this number,
        /// those are manually merged into the <see cref="Boundary"/> after other boxes are ready.
        /// Computed by implementations of <see cref="LayoutStrategyBase.PreProcessThisNode"/>.
        /// </summary>
        public int NumberOfSiblings;

        /// <summary>
        /// Number of sibling rows. Used by strategies that arrange box's immediate children into more than one line.
        /// Meaning of "row" may differ.
        /// Computed by implementations of <see cref="LayoutStrategyBase.PreProcessThisNode"/>.
        /// </summary>
        public int NumberOfSiblingRows;

        /// <summary>
        /// Number of sibling columns. Used by strategies that arrange box's immediate children into more than one column.
        /// Meaning of "column" may differ, e.g. it may include one or more boxes per each logical row.
        /// Computed by implementations of <see cref="LayoutStrategyBase.PreProcessThisNode"/>.
        /// </summary>
        public int NumberOfSiblingColumns;

        /// <summary>
        /// Position of corresponding Box.
        /// </summary>
        public Point TopLeft;
        /// <summary>
        /// Size of corresponding Box.
        /// </summary>
        public Size Size;

        /// <summary>
        /// Left edge of the bounding rectangle.
        /// </summary>
        public double Left => TopLeft.X;
        /// <summary>
        /// Right edge of the bounding rectangle.
        /// </summary>
        public double Right => TopLeft.X + Size.Width;
        /// <summary>
        /// Top edge of the bounding rectangle.
        /// </summary>
        public double Top => TopLeft.Y;
        /// <summary>
        /// Bottom edge of the bounding rectangle.
        /// </summary>
        public double Bottom => TopLeft.Y + Size.Height;
        /// <summary>
        /// Horizontal center.
        /// </summary>
        public double CenterH => TopLeft.X + Size.Width / 2;

        /// <summary>
        /// Vertical center.
        /// </summary>
        public double CenterV => TopLeft.Y + Size.Height / 2;

        /// <summary>
        /// External boundaries of this branch, updated by <see cref="LayoutAlgorithm"/> 
        /// after each merge of <see cref="Boundary"/> containing children boxes.
        /// </summary>
        public Rect BranchExterior;

        /// <summary>
        /// Exterior vertical boundaries of the layout row of siblings of this frame.
        /// </summary>
        public Dimensions SiblingsRowV;

        /// <summary>
        /// Connectors to dependent objects.
        /// </summary>
        [CanBeNull]
        public Connector Connector;

        private LayoutStrategyBase m_effectiveLayoutStrategy;
    }
}