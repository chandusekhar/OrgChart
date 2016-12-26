using System;
using System.Collections.Generic;
using Staffer.OrgChart.Annotations;

namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// Arranges child boxes in a single vertical column under the parent, 
    /// somewhat offset to the left or to the right, depending on <see cref="LayoutStrategyBase.ParentAlignment"/>.
    /// Cannot be configured to position parent in the middle of children.
    /// Children are attached to a central vertical carrier going from parent's bottom.
    /// </summary>
    public class SingleColumnLayoutStrategy : LayoutStrategyBase
    {
        /// <summary>
        /// A chance for layout strategy to append special auto-generated boxes into the visual tree. 
        /// </summary>
        public override void PreProcessThisNode([NotNull]LayoutState state, [NotNull] BoxTree.TreeNode node)
        {
            if (ParentAlignment != BranchParentAlignment.Left
                && ParentAlignment != BranchParentAlignment.Right)
            {
                throw new InvalidOperationException("Unsupported value for " + nameof(ParentAlignment));
            }

            node.State.NumberOfSiblings = node.Element.IsCollapsed ? 0 : node.ChildCount;

            // only add spacers for non-collapsed boxes
            if (node.State.NumberOfSiblings > 0 && node.Level > 0)
            {
                // add one (for vertical spacer) into the count of layout columns
                node.State.NumberOfSiblingColumns = 1;
                node.State.NumberOfSiblingRows = node.ChildCount;

                // add parent's vertical carrier to the end
                var verticalSpacer = Box.Special(Box.None, node.Element.Id, false);
                node.AddRegularChild(verticalSpacer);
            }
        }

        /// <summary>
        /// Applies layout changes to a given box and its children.
        /// </summary>
        public override void ApplyVerticalLayout([NotNull]LayoutState state, [NotNull]LayoutState.LayoutLevel level)
        {
            var node = level.BranchRoot;

            if (node.Level == 0)
            {
                node.State.SiblingsRowV = new Dimensions(node.State.Top, node.State.Bottom);
            }

            if (node.AssistantsRoot != null)
            {
                // assistants root has to be initialized with main node's exterior 
                node.AssistantsRoot.State.CopyExteriorFrom(node.State);
                LayoutAlgorithm.VerticalLayout(state, node.AssistantsRoot);
            }

            var prevRowExterior = new Dimensions(
                node.State.SiblingsRowV.From,
                node.AssistantsRoot == null
                ? node.State.SiblingsRowV.To
                : node.State.BranchExterior.Bottom);

            for (var row = 0; row < node.State.NumberOfSiblings; row++)
            {
                // first, compute
                var child = node.Children[row];
                var rect = child.State;

                var top = prevRowExterior.To + ParentChildSpacing;
                child.State.TopLeft = new Point(
                    rect.Left,
                    top);
                child.State.Size = new Size(
                    rect.Size.Width,
                    rect.Size.Height);
                child.State.BranchExterior = new Rect(
                    child.State.TopLeft,
                    child.State.Size);

                var rowExterior = new Dimensions(top, top + rect.Size.Height);

                child = node.Children[row];
                child.State.SiblingsRowV = rowExterior;

                // re-enter layout algorithm for child branch
                LayoutAlgorithm.VerticalLayout(state, child);

                var childBranchBottom = child.State.BranchExterior.Bottom;

                prevRowExterior = new Dimensions(rowExterior.From, Math.Max(childBranchBottom, rowExterior.To));
            }
        }

        /// <summary>
        /// Applies layout changes to a given box and its children.
        /// </summary>
        public override void ApplyHorizontalLayout([NotNull] LayoutState state, [NotNull]LayoutState.LayoutLevel level)
        {
            var node = level.BranchRoot;

            var nodeState = node.State;

            if (node.AssistantsRoot != null)
            {
                LayoutAlgorithm.HorizontalLayout(state, node.AssistantsRoot);
            }

            // first, perform horizontal layout for every node in this column
            for (var row = 0; row < nodeState.NumberOfSiblings; row++)
            {
                var child = node.Children[row];

                // re-enter layout algorithm for child branch
                // siblings are guaranteed not to offend each other
                LayoutAlgorithm.HorizontalLayout(state, child);
            }

            // now align the column
            var edges = LayoutAlgorithm.AlignHorizontalCenters(state, level, EnumerateColumn(node));

            if (node.Level > 0 && node.ChildCount > 0)
            {
                var rect = node.State;
                double diff;
                if (ParentAlignment == BranchParentAlignment.Left)
                {
                    var desiredLeft = rect.CenterH + ParentConnectorShield/2;
                    diff = desiredLeft - edges.From;
                }
                else if (ParentAlignment == BranchParentAlignment.Right)
                {
                    var desiredRight = rect.CenterH - ParentConnectorShield/2;
                    diff = desiredRight - edges.To;
                }
                else
                {
                    throw new InvalidOperationException("Invalid ParentAlignment setting");
                }

                // vertical connector from parent
                LayoutAlgorithm.MoveChildrenOnly(state, level, diff);

                // spacer for the vertical carrier 
                var verticalSpacer = node.Level > 0 ? node.Children[node.ChildCount - 1] : null;
                if (verticalSpacer != null)
                {
                    var spacerTop = node.State.Bottom;
                    var spacerBottom = node.Children[node.ChildCount - 2].State.Bottom;
                    verticalSpacer.State.TopLeft = new Point(
                        rect.CenterH - ParentConnectorShield/2,
                        spacerTop);
                    verticalSpacer.State.Size = new Size(
                        ParentConnectorShield,
                        spacerBottom - spacerTop);
                    verticalSpacer.State.BranchExterior = new Rect(
                        verticalSpacer.State.TopLeft, 
                        verticalSpacer.State.Size);
                    state.MergeSpacer(verticalSpacer);
                }
            }
        }

        private IEnumerable<BoxTree.TreeNode> EnumerateColumn(BoxTree.TreeNode branchRoot)
        {
            for (var i = 0; i < branchRoot.State.NumberOfSiblings; i++)
            {
                yield return branchRoot.Children[i];
            }
        }

        /// <summary>
        /// Allocates and routes connectors.
        /// </summary>
        public override void RouteConnectors([NotNull] LayoutState state, [NotNull] BoxTree.TreeNode node)
        {
            if (node.ChildCount == 0)
            {
                return;
            }

            // one parent connector (also serves as mid-sibling carrier) and horizontal carriers
            var count = 1 + node.State.NumberOfSiblings;

            var segments = new Edge[count];

            var rootRect = node.State;
            var center = rootRect.CenterH;

            var verticalCarrierHeight = node.Children[node.State.NumberOfSiblings - 1].State.CenterV - node.State.Bottom;

            // big vertical connector, from parent to last row
            segments[0] = new Edge(new Point(center, rootRect.Bottom), new Point(center, rootRect.Bottom + verticalCarrierHeight));

            for (var ix = 0; ix < node.State.NumberOfSiblings; ix++)
            {
                var rect = node.Children[ix].State;
                var destination = ParentAlignment == BranchParentAlignment.Left ? rect.Left : rect.Right;
                segments[1 + ix] = new Edge(
                    new Point(center, rect.CenterV),
                    new Point(destination, rect.CenterV));
            }

            node.State.Connector = new Connector(segments);
        }
    }
}