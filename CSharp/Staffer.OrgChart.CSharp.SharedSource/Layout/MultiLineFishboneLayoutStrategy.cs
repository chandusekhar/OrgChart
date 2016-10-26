using System;
using System.Collections.Generic;
using Staffer.OrgChart.Annotations;
using Staffer.OrgChart.Misc;

namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// Arranges child boxes in multiple vertically stretched groups, stuffed onto "fish bones" on left and right sides of vertical carriers,
    /// with only one main horizontal carrier going under parent's bottom, connecting all vertical carriers.
    /// Can only be configured to position parent in the middle of children.
    /// </summary>
    public class MultiLineFishboneLayoutStrategy : LinearLayoutStrategy
    {
        /// <summary>
        /// Maximum number of boxes staffed onto a single vertical carrier.
        /// </summary>
        public int MaxGroups = 4;

        /// <summary>
        /// A chance for layout strategy to append special auto-generated boxes into the visual tree. 
        /// </summary>
        public override void PreProcessThisNode([NotNull]LayoutState state, [NotNull] Tree<int, Box, NodeLayoutInfo>.TreeNode node)
        {
            if (ParentAlignment != BranchParentAlignment.Center)
            {
                throw new InvalidOperationException("Unsupported value for " + nameof(ParentAlignment));
            }

            if (MaxGroups <= 0)
            {
                throw new InvalidOperationException(nameof(MaxGroups) + " must be a positive value");
            }

            if (node.ChildCount <= MaxGroups*2)
            {
                base.PreProcessThisNode(state, node);
                return;
            }

            node.State.NumberOfSiblings = node.ChildCount;

            // only add spacers for non-collapsed boxes
            if (node.State.NumberOfSiblings > 0)
            {
                // using column == group here, 
                // and each group consists of two vertical stretches of boxes with a vertical carrier in between
                node.State.NumberOfSiblingColumns = MaxGroups;
                node.State.NumberOfSiblingRows = node.State.NumberOfSiblings/(MaxGroups*2);
                if (node.State.NumberOfSiblings%(MaxGroups*2) != 0)
                {
                    node.State.NumberOfSiblingRows++;
                }

                // a connector from parent to horizontal carrier
                var parentSpacer = Box.Special(Box.None, node.Element.Id, false);
                node.AddChild(parentSpacer);

                // spacers for vertical carriers 
                for (var i = 0; i < node.State.NumberOfSiblingColumns; i++)
                {
                    var verticalSpacer = Box.Special(Box.None, node.Element.Id, false);
                    node.AddChild(verticalSpacer);
                }

                // if needed, horizontal carrier 
                if (node.State.NumberOfSiblingColumns > 1)
                {
                    var horizontalSpacer = Box.Special(Box.None, node.Element.Id, false);
                    node.AddChild(horizontalSpacer);
                }
            }
        }

        /// <summary>
        /// Applies layout changes to a given box and its children.
        /// </summary>
        public override void ApplyVerticalLayout([NotNull]LayoutState state, LayoutState.LayoutLevel level)
        {
            if (level.BranchRoot.State.NumberOfSiblings <= MaxGroups * 2)
            {
                base.ApplyVerticalLayout(state, level);
                return;
            }

            var node = level.BranchRoot;
            if (node.Level == 0)
            {
                node.Element.Frame.SiblingsRowV = new Dimensions(node.Element.Frame.Exterior.Top, node.Element.Frame.Exterior.Bottom);
            }

            var adapter = new SingleFishboneLayoutAdapter(node);
            while (adapter.NextGroup())
            {   
                LayoutAlgorithm.VerticalLayout(state, adapter.SpecialRoot);
            }
        }

        /// <summary>
        /// Applies layout changes to a given box and its children.
        /// </summary>
        public override void ApplyHorizontalLayout([NotNull]LayoutState state, LayoutState.LayoutLevel level)
        {
            if (level.BranchRoot.State.NumberOfSiblings <= MaxGroups * 2)
            {
                base.ApplyHorizontalLayout(state, level);
                return;
            }

            var node = level.BranchRoot;
            if (node.Level == 0)
            {
                node.Element.Frame.SiblingsRowV = new Dimensions(node.Element.Frame.Exterior.Top, node.Element.Frame.Exterior.Bottom);
            }

            var adapter = new SingleFishboneLayoutAdapter(node);
            while (adapter.NextGroup())
            {
                LayoutAlgorithm.HorizontalLayout(state, adapter.SpecialRoot);
            }

            var rect = node.Element.Frame.Exterior;

            if (ParentAlignment == BranchParentAlignment.Center)
            {
                if (node.Level > 0)
                {
                    double diff;
                    if (node.State.NumberOfSiblingColumns > 1)
                    {
                        var leftCarrier = node.Children[node.State.NumberOfSiblings + 1].Element.Frame.Exterior.CenterH;
                        var rightCarrier = node.Children[node.State.NumberOfSiblings + node.State.NumberOfSiblingColumns].Element.Frame.Exterior.CenterH;
                        var desiredCenter = (leftCarrier + rightCarrier) / 2.0;
                        diff = rect.CenterH - desiredCenter;
                    }
                    else
                    {
                        var carrier = node.Children[1 + node.State.NumberOfSiblings].Element.Frame.Exterior.CenterH;
                        var desiredCenter = rect.CenterH;
                        diff = desiredCenter - carrier;
                    }
                    LayoutAlgorithm.MoveChildrenOnly(state, level, diff);
                }
            }
            else
            {
                throw new InvalidOperationException("Invalid ParentAlignment setting");
            }

            if (node.Level > 0)
            {
                // vertical connector from parent
                var ix = node.State.NumberOfSiblings;
                var verticalSpacerBox = node.Children[ix].Element;
                verticalSpacerBox.Frame.Exterior = new Rect(
                    rect.CenterH - ParentConnectorShield/2,
                    rect.Bottom,
                    ParentConnectorShield,
                    node.Children[0].Element.Frame.SiblingsRowV.From - rect.Bottom);
                verticalSpacerBox.Frame.BranchExterior = verticalSpacerBox.Frame.Exterior;
                state.MergeSpacer(verticalSpacerBox);
                ix++;

                // vertical carriers already merged in
                ix += node.State.NumberOfSiblingColumns;

                if (node.State.NumberOfSiblingColumns > 1)
                {
                    // have a horizontal carrier
                    var horizontalSpacerBox = node.Children[ix].Element;
                    var leftmost = node.Children[node.State.NumberOfSiblings + 1].Element.Frame.Exterior.TopLeft;
                    var rightmost = node.Children[ix - 1].Element.Frame.Exterior.Right;
                    horizontalSpacerBox.Frame.Exterior = new Rect(
                        leftmost.X, leftmost.Y - ParentChildSpacing,
                        rightmost - leftmost.X, ParentChildSpacing);
                    horizontalSpacerBox.Frame.BranchExterior = horizontalSpacerBox.Frame.Exterior;
                    state.MergeSpacer(horizontalSpacerBox);
                }
            }
        }

        /// <summary>
        /// Allocates and routes connectors.
        /// </summary>
        public override void RouteConnectors([NotNull] LayoutState state, [NotNull] Tree<int, Box, NodeLayoutInfo>.TreeNode node)
        {
            if (node.State.NumberOfSiblings <= MaxGroups * 2)
            {
                base.RouteConnectors(state, node);
                return;
            }

            var count = 1 // one parent connector
                        + node.State.NumberOfSiblings // one hook for each child
                        + node.State.NumberOfSiblingColumns; // one for each vertical carrier
            if (node.State.NumberOfSiblingColumns > 1)
            {
                // also have a horizontal carrier
                count++;
            }

            var segments = new Edge[count];

            var rootRect = node.Element.Frame.Exterior;
            var center = rootRect.CenterH;

            var ix = 0;

            // parent connector
            var space = node.Children[0].Element.Frame.SiblingsRowV.From - rootRect.Bottom;
            segments[ix++] = new Edge(new Point(center, rootRect.Bottom),
                new Point(center, rootRect.Bottom + space - ChildConnectorHookLength));

            // one hook for each child
            var iterator = new SingleFishboneLayoutAdapter.GroupIterator(node.State.NumberOfSiblings, node.State.NumberOfSiblingColumns);
            while (iterator.NextGroup())
            {
                var carrier = node.Children[1 + node.State.NumberOfSiblings + iterator.Group].Element.Frame.Exterior;
                var from = carrier.CenterH;

                var isLeft = true;
                var countOnThisSide = 0;
                for (var i = iterator.FromIndex; i < iterator.FromIndex + iterator.Count; i++)
                {
                    var to = isLeft ? node.Children[i].Element.Frame.Exterior.Right : node.Children[i].Element.Frame.Exterior.Left;
                    var y = node.Children[i].Element.Frame.Exterior.CenterV;
                    segments[ix++] = new Edge(new Point(from, y), new Point(to, y));

                    if (++countOnThisSide == iterator.MaxOnLeft)
                    {
                        countOnThisSide = 0;
                        if (isLeft)
                        {
                            // one for each vertical carrier
                            segments[1 + node.State.NumberOfSiblings + iterator.Group] = new Edge(
                                new Point(carrier.CenterH, carrier.Top - ChildConnectorHookLength),
                                new Point(carrier.CenterH, node.Children[i].Element.Frame.Exterior.CenterV));
                        }
                        isLeft = !isLeft;
                    }
                }
            }

            // vertical carriers already created
            ix += node.State.NumberOfSiblingColumns;

            if (node.State.NumberOfSiblingColumns > 1)
            {
                var leftGroup = node.Children[1 + node.State.NumberOfSiblings].Element.Frame.Exterior;
                var rightGroup = node.Children[1 + node.State.NumberOfSiblings + node.State.NumberOfSiblingColumns - 1].Element.Frame.Exterior;

                // one horizontal carrier
                segments[ix] = new Edge(
                    new Point(leftGroup.CenterH, leftGroup.Top - ChildConnectorHookLength),
                    new Point(rightGroup.CenterH, rightGroup.Top - ChildConnectorHookLength));
            }

            node.Element.Frame.Connector = new Connector(segments);
        }

        /// <summary>
        /// Implements layout for a single vertically stretched fishbone.
        /// Re-used by <see cref="MultiLineFishboneLayoutStrategy"/> to layout multiple groups of siblings.
        /// </summary>
        private class SingleFishboneLayoutAdapter : LayoutStrategyBase
        {
            public class GroupIterator
            {
                private readonly int m_numberOfSiblings;
                private readonly int m_numberOfGroups;

                public int Group;
                public int FromIndex;
                public int Count;
                public int MaxOnLeft;

                public GroupIterator(int numberOfSiblings, int numberOfGroups)
                {
                    m_numberOfSiblings = numberOfSiblings;
                    m_numberOfGroups = numberOfGroups;
                }

                public int CountInGroup()
                {
                    var countInRow = m_numberOfGroups * 2;

                    var result = 0;
                    var countToThisGroup = Group * 2 + 2;
                    var firstInRow = 0;
                    while (true)
                    {
                        var countInThisRow = firstInRow >= m_numberOfSiblings - countInRow ? m_numberOfSiblings - firstInRow : countInRow;
                        if (countInThisRow >= countToThisGroup)
                        {
                            result += 2;
                        }
                        else
                        {
                            countToThisGroup--;
                            if (countInThisRow >= countToThisGroup)
                            {
                                result++;
                            }
                            break;
                        }
                        firstInRow += countInRow;
                    }

                    return result;
                }

                public bool NextGroup()
                {
                    FromIndex = FromIndex + Count;

                    if (FromIndex > 0)
                    {
                        Group++;
                    }
                    Count = CountInGroup();
                    MaxOnLeft = Count / 2 + Count % 2;
                    return Count != 0;
                }
            }

            public class TreeNodeView : Tree<int, Box, NodeLayoutInfo>.TreeNode
            {
                public TreeNodeView([NotNull] Box element) : base(element)
                {
                }

                public void Prepare(int capacity)
                {
                    if (Children == null)
                    {
                        Children = new List<Tree<int, Box, NodeLayoutInfo>.TreeNode>(capacity);
                    }
                    else
                    {
                        Children.Clear();
                    }
                }

                public void AddChildView(Tree<int, Box, NodeLayoutInfo>.TreeNode node)
                {
                    Children.Add(node);
                }
            }

            public readonly Tree<int, Box, NodeLayoutInfo>.TreeNode RealRoot;
            public readonly TreeNodeView SpecialRoot;
            public readonly GroupIterator Iterator;

            public SingleFishboneLayoutAdapter([NotNull]Tree<int, Box, NodeLayoutInfo>.TreeNode realRoot)
            {
                Iterator = new GroupIterator(realRoot.State.NumberOfSiblings, realRoot.State.NumberOfSiblingColumns);

                RealRoot = realRoot;
                SpecialRoot = new TreeNodeView(Box.Special(Box.None, realRoot.Element.Id, true))
                {
                    Level = RealRoot.Level,
                    ParentNode = RealRoot
                };

                SpecialRoot.State.EffectiveLayoutStrategy = this;

                var parentStrategy = (MultiLineFishboneLayoutStrategy)realRoot.State.RequireLayoutStrategy();
                SiblingSpacing = parentStrategy.SiblingSpacing;
                ParentConnectorShield = parentStrategy.ParentConnectorShield;
                ParentChildSpacing = parentStrategy.ParentChildSpacing;
                ParentAlignment = parentStrategy.ParentAlignment;
                ChildConnectorHookLength = parentStrategy.ChildConnectorHookLength;
            }

            public bool NextGroup()
            {
                if (!Iterator.NextGroup())
                {
                    return false;
                }

                SpecialRoot.State.NumberOfSiblings = Iterator.Count;
                SpecialRoot.Prepare(RealRoot.State.NumberOfSiblingRows * 2);

                for (var i = 0; i < Iterator.Count; i++)
                {
                    SpecialRoot.AddChildView(RealRoot.Children[Iterator.FromIndex + i]);
                }
                var spacer = RealRoot.Children[RealRoot.State.NumberOfSiblings + 1 + Iterator.Group];
                SpecialRoot.AddChildView(spacer);

                SpecialRoot.Element.Frame.Exterior = RealRoot.Element.Frame.Exterior;
                SpecialRoot.Element.Frame.BranchExterior = RealRoot.Element.Frame.Exterior;
                SpecialRoot.Element.Frame.SiblingsRowV = RealRoot.Element.Frame.SiblingsRowV;

                return true;
            }

            public override void PreProcessThisNode(LayoutState state, Tree<int, Box, NodeLayoutInfo>.TreeNode node)
            {
                throw new NotSupportedException();
            }

            public override void ApplyVerticalLayout(LayoutState state, LayoutState.LayoutLevel level)
            {
                var prevRowBottom = SpecialRoot.Element.Frame.SiblingsRowV.To;
                
                for (var i = 0; i < Iterator.MaxOnLeft; i++)
                {
                    var child = SpecialRoot.Children[i];
                    var frame = child.Element.Frame;
                    var rect = frame.Exterior;
                    frame.Exterior = new Rect(rect.Left, prevRowBottom + ParentChildSpacing, rect.Size.Width, rect.Size.Height);

                    var rowExterior = new Dimensions(frame.Exterior.Top, frame.Exterior.Bottom);

                    var i2 = i + Iterator.MaxOnLeft;
                    if (i2 < Iterator.Count)
                    {
                        var child2 = SpecialRoot.Children[i2];
                        var frame2 = child2.Element.Frame;
                        var rect2 = child2.Element.Frame.Exterior;
                        frame2.Exterior = new Rect(rect2.Left, prevRowBottom + ParentChildSpacing, rect2.Size.Width, rect2.Size.Height);

                        if (frame2.Exterior.Bottom > frame.Exterior.Bottom)
                        {
                            frame.Exterior = new Rect(rect.Left, frame2.Exterior.CenterV - rect.Size.Height/2, rect.Size.Width, rect.Size.Height);
                        }
                        else if (frame2.Exterior.Bottom < frame.Exterior.Bottom)
                        {
                            frame2.Exterior = new Rect(rect2.Left, frame.Exterior.CenterV - rect2.Size.Height / 2, rect2.Size.Width, rect2.Size.Height);
                        }

                        frame2.BranchExterior = frame2.Exterior;
                        rowExterior += new Dimensions(frame2.Exterior.Top, frame2.Exterior.Bottom + state.Diagram.LayoutSettings.BoxVerticalMargin);

                        frame2.SiblingsRowV = rowExterior;
                        LayoutAlgorithm.VerticalLayout(state, child2);
                        prevRowBottom = frame2.BranchExterior.Bottom;
                    }

                    frame.BranchExterior = frame.Exterior;
                    frame.SiblingsRowV = rowExterior;
                    LayoutAlgorithm.VerticalLayout(state, child);
                    prevRowBottom = Math.Max(prevRowBottom, frame.BranchExterior.Bottom);
                }
            }

            public override void ApplyHorizontalLayout(LayoutState state, LayoutState.LayoutLevel level)
            {
                if (level.BranchRoot != SpecialRoot)
                {
                    throw new InvalidOperationException("Wrong root node received");
                }

                var left = true;
                var countOnThisSide = 0;
                for (var i = 0; i < Iterator.Count; i++)
                {
                    var child = SpecialRoot.Children[i];
                    LayoutAlgorithm.HorizontalLayout(state, child);

                    // we go top-bottom to layout left side of the group,
                    // then add a carrier protector
                    // then top-bottom to fill right side of the group
                    if (++countOnThisSide == Iterator.MaxOnLeft)
                    {
                        if (left)
                        {
                            // horizontally align children in left pillar
                            LayoutAlgorithm.AlignHorizontalCenters(state, level, EnumerateSiblings(0, Iterator.MaxOnLeft));

                            left = false;
                            countOnThisSide = 0;

                            var rightmost = double.MinValue;
                            for (var k = 0; k < i; k++)
                            {
                                rightmost = Math.Max(rightmost, SpecialRoot.Children[k].Element.Frame.BranchExterior.Right);
                            }

                            // vertical spacer does not have to be extended to the bottom of the lowest branch,
                            // unless the lowest branch on the right side has some children and is expanded
                            if (Iterator.Count %2 != 0)
                            {
                                rightmost = Math.Max(rightmost, child.Element.Frame.Exterior.Right);
                            }
                            else
                            {
                                var opposite = SpecialRoot.Children[SpecialRoot.State.NumberOfSiblings - 1];
                                if (opposite.Element.IsCollapsed || opposite.ChildCount == 0)
                                {
                                    rightmost = Math.Max(rightmost, child.Element.Frame.Exterior.Right);
                                }
                                else
                                {
                                    rightmost = Math.Max(rightmost, child.Element.Frame.BranchExterior.Right);
                                }
                            }

                            // integrate protector for group's vertical carrier 
                            var spacer = SpecialRoot.Children[SpecialRoot.State.NumberOfSiblings].Element;
                            spacer.Frame.Exterior = new Rect(
                                rightmost,
                                SpecialRoot.Children[0].Element.Frame.SiblingsRowV.From,
                                SiblingSpacing,
                                child.Element.Frame.SiblingsRowV.To - SpecialRoot.Children[0].Element.Frame.SiblingsRowV.From
                                );
                            spacer.Frame.BranchExterior = spacer.Frame.Exterior;
                            level.Boundary.MergeFrom(spacer);
                        }
                        else
                        {
                            // horizontally align children in right pillar
                            LayoutAlgorithm.AlignHorizontalCenters(state, level, EnumerateSiblings(Iterator.MaxOnLeft, Iterator.Count));
                        }
                    }
                }
            }

            private IEnumerable<Tree<int, Box, NodeLayoutInfo>.TreeNode> EnumerateSiblings(int from, int to)
            {
                for (var i = from; i < to; i++)
                {
                    yield return SpecialRoot.Children[i];
                }
            }

            public override void RouteConnectors(LayoutState state, Tree<int, Box, NodeLayoutInfo>.TreeNode node)
            {
                throw new NotSupportedException();
            }
        }
    }
}