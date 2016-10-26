﻿using System;
using System.Collections.Generic;
using System.Linq;
using Staffer.OrgChart.Annotations;
using Staffer.OrgChart.Misc;

namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// Applies layout.
    /// </summary>
    public static class LayoutAlgorithm
    {
        /// <summary>
        /// Computes bounding rectangle in diagram space using only visible (non-autogenerated boxes).
        /// Useful for rendering the chart, as boxes frequently go into negative side horizontally, and have a special root box on top - all of those should not be accounted for.
        /// </summary>
        public static Rect ComputeBranchVisualBoundingRect([NotNull]Tree<int, Box, NodeLayoutInfo> visualTree)
        {
            var result = new Rect();
            var initialized = false;

            Tree<int, Box, NodeLayoutInfo>.TreeNode.IterateParentFirst(visualTree.Roots[0], node =>
            {
                var box = node.Element;

                if (box.AffectsLayout && !box.IsSpecial)
                {
                    if (initialized)
                    {
                        result += box.Frame.Exterior;
                    }
                    else
                    {
                        initialized = true;
                        result = box.Frame.Exterior;
                    }
                }

                return !box.IsCollapsed;
            });

            return result;
        }

        /// <summary>
        /// Resets chart layout-related properties on boxes, possibly present from previous layout runs.
        /// Wipes out connectors too.
        /// </summary>
        public static void ResetBoxPositions([NotNull]Diagram diagram)
        {
            foreach (var box in diagram.Boxes.BoxesById.Values)
            {
                box.Frame.ResetLayout();
            }
        }

        /// <summary>
        /// Initializes <paramref name="state"/> and performs all layout operations.
        /// </summary>
        public static void Apply([NotNull]LayoutState state)
        {
            // verify the root
            if (state.Diagram.Boxes.SystemRoot == null)
            {
                throw new InvalidOperationException("SystemRoot is not initialized on the box container");
            }

            state.CurrentOperation = LayoutState.Operation.Preparing;

            var tree = Tree<int, Box, NodeLayoutInfo>.Build(state.Diagram.Boxes.BoxesById.Values, x => x.Id, x => x.ParentId);

            state.Diagram.VisualTree = tree;

            // verify the root
            if (tree.Roots.Count != 1 || tree.Roots[0].Element.Id != state.Diagram.Boxes.SystemRoot.Id)
            {
                throw new Exception("SystemRoot is not on the top of the visual tree");
            }

            // set the tree and update visibility
            tree.UpdateHierarchyStats();
            state.AttachVisualTree(tree);

            if (state.BoxSizeFunc != null)
            {
                // apply box sizes
                foreach (var box in state.Diagram.Boxes.BoxesById.Values.Where(x => x.IsDataBound))
                {
                    box.Frame.Exterior = new Rect(state.BoxSizeFunc(box.DataId));
                }
            }

            // update visibility of boxes based on collapsed state
            tree.IterateParentFirst(
                node =>
                {
                    node.Element.AffectsLayout =
                        node.ParentNode == null ||
                        node.ParentNode.Element.AffectsLayout && !node.ParentNode.Element.IsCollapsed;
                    return true;
                });

            state.CurrentOperation = LayoutState.Operation.PreprocessVisualTree;
            PreprocessVisualTree(state, tree);

            state.CurrentOperation = LayoutState.Operation.VerticalLayout;
            VerticalLayout(state, tree.Roots[0]);

            state.CurrentOperation = LayoutState.Operation.HorizontalLayout;
            HorizontalLayout(state, tree.Roots[0]);

            state.CurrentOperation = LayoutState.Operation.ConnectorsLayout;
            RouteConnectors(state, tree);

            state.CurrentOperation = LayoutState.Operation.Completed;
        }

        private static void PreprocessVisualTree([NotNull]LayoutState state, [NotNull]Tree<int, Box, NodeLayoutInfo> visualTree)
        {
            visualTree.IterateParentFirst(node =>
            {
                if (node.Element.IsSpecial && node.Level > 0)
                {
                    return false;
                }

                // first, find and associate layout strategy in effect for this node
                if (node.Element.LayoutStrategyId != null)
                {
                    // is it explicitly specified?
                    node.State.EffectiveLayoutStrategy = state.Diagram.LayoutSettings.LayoutStrategies[node.Element.LayoutStrategyId];
                }
                else if (node.ParentNode != null)
                {
                    // can we inherit it from previous level?
                    node.State.EffectiveLayoutStrategy = node.ParentNode.State.RequireLayoutStrategy();
                }
                else
                {
                    node.State.EffectiveLayoutStrategy = state.Diagram.LayoutSettings.RequireDefaultLayoutStrategy();
                }

                // now let it pre-allocate special boxes etc
                node.State.RequireLayoutStrategy().PreProcessThisNode(state, node);

                return !node.Element.IsCollapsed && node.ChildCount > 0;
            });
        }

        /// <summary>
        /// Re-entrant layout algorithm,
        /// </summary>
        public static void HorizontalLayout([NotNull]LayoutState state, [NotNull]Tree<int, Box, NodeLayoutInfo>.TreeNode branchRoot)
        {
            if (!branchRoot.Element.AffectsLayout)
            {
                throw new InvalidOperationException($"Branch root {branchRoot.Element.Id} does not affect layout");
            }

            var level = state.PushLayoutLevel(branchRoot);
            try
            {
                if (branchRoot.State.NumberOfSiblings > 0 && !branchRoot.Element.IsCollapsed)
                {
                    branchRoot.State.RequireLayoutStrategy().ApplyHorizontalLayout(state, level);
                }
            }
            finally
            {
                state.PopLayoutLevel();
            }
        }

        /// <summary>
        /// Re-entrant layout algorithm.
        /// </summary>
        public static void VerticalLayout([NotNull]LayoutState state, [NotNull]Tree<int, Box, NodeLayoutInfo>.TreeNode branchRoot)
        {
            if (!branchRoot.Element.AffectsLayout)
            {
                throw new InvalidOperationException($"Branch root {branchRoot.Element.Id} does not affect layout");
            }

            var level = state.PushLayoutLevel(branchRoot);
            try
            {
                if (branchRoot.State.NumberOfSiblings > 0 && !branchRoot.Element.IsCollapsed)
                {
                    branchRoot.State.RequireLayoutStrategy().ApplyVerticalLayout(state, level);
                }
            }
            finally
            {
                state.PopLayoutLevel();
            }
        }

        private static void RouteConnectors([NotNull]LayoutState state, [NotNull]Tree<int, Box, NodeLayoutInfo> visualTree)
        {
            visualTree.IterateParentFirst(node =>
            {
                if (node.Element.IsCollapsed || node.State.NumberOfSiblings == 0)
                {
                    return false;
                }

                if (node.Level == 0)
                {
                    return true;
                }

                if (!node.Element.IsSpecial)
                {
                    node.State.RequireLayoutStrategy().RouteConnectors(state, node);
                    return true;
                }

                return false;
            });
        }

        /// <summary>
        /// Moves a given branch horizontally, except its root box.
        /// Also updates branch exterior rects.
        /// Also updates branch boundary for the current <paramref name="layoutLevel"/>.
        /// </summary>
        public static void MoveChildrenOnly([NotNull]LayoutState state, LayoutState.LayoutLevel layoutLevel, double offset)
        {
            var children = layoutLevel.BranchRoot.Children;
            if (children == null || children.Count == 0)
            {
                throw new InvalidOperationException("Should never be invoked when children not set");
            }

            foreach (var child in children)
            {
                Tree<int, Box, NodeLayoutInfo>.TreeNode.IterateChildFirst(child,
                    node =>
                    {
                        if (node.Element.AffectsLayout)
                        {
                            node.Element.Frame.Exterior = node.Element.Frame.Exterior.MoveH(offset);
                            node.Element.Frame.BranchExterior = node.Element.Frame.BranchExterior.MoveH(offset);
                        }
                        return true;
                    });
            }

            layoutLevel.Boundary.ReloadFromBranch(layoutLevel.BranchRoot);
            layoutLevel.BranchRoot.Element.Frame.BranchExterior = layoutLevel.Boundary.BoundingRect;
        }

        /// <summary>
        /// Moves a given branch horizontally, except its root box.
        /// Also updates branch exterior rects.
        /// Unlike <see cref="MoveChildrenOnly"/> and <see cref="MoveBranch"/>, does NOT update the boundary.
        /// </summary>
        /// <remarks>DOES NOT update branch boundary! Must call <see cref="Boundary.ReloadFromBranch"/> after batch of updates is complete</remarks>
        public static void MoveOneChild([NotNull]LayoutState state, [NotNull]Tree<int, Box, NodeLayoutInfo>.TreeNode root, double offset)
        {
            Tree<int, Box, NodeLayoutInfo>.TreeNode.IterateChildFirst(root,
                node =>
                {
                    if (node.Element.AffectsLayout)
                    {
                        node.Element.Frame.Exterior = node.Element.Frame.Exterior.MoveH(offset);
                        node.Element.Frame.BranchExterior = node.Element.Frame.BranchExterior.MoveH(offset);
                    }
                    return true;
                });
        }

        /// <summary>
        /// Moves a given branch horizontally, including its root box.
        /// Also updates branch exterior rects.
        /// Also updates branch boundary for the current <paramref name="layoutLevel"/>.
        /// </summary>
        public static void MoveBranch([NotNull]LayoutState state, LayoutState.LayoutLevel layoutLevel, double offset)
        {
            MoveOneChild(state, layoutLevel.BranchRoot, offset);
            layoutLevel.Boundary.ReloadFromBranch(layoutLevel.BranchRoot);
            layoutLevel.BranchRoot.Element.Frame.BranchExterior = layoutLevel.Boundary.BoundingRect;
        }

        /// <summary>
        /// Vertically aligns a subset of child nodes, presumably located one above another.
        /// All children must belong to the current layout level's root.
        /// Returns leftmost and rightmost boundaries of all branches in the <paramref name="subset"/>, after alignment.
        /// </summary>
        public static Dimensions AlignHorizontalCenters(
            [NotNull]LayoutState state, LayoutState.LayoutLevel level,
            [NotNull]IEnumerable<Tree<int, Box, NodeLayoutInfo>.TreeNode> subset)
        {
            // compute the rightmost center in the column
            var center = double.MinValue;
            foreach (var child in subset)
            {
                var c = child.Element.Frame.Exterior.CenterH;
                if (c > center)
                {
                    center = c;
                }
            }

            // move those boxes in the column that are not aligned with the rightmost center
            var leftmost = double.MaxValue;
            var rightmost = double.MinValue;
            foreach (var child in subset)
            {
                var frame = child.Element.Frame;
                var c = frame.Exterior.CenterH;
                if (c != center)
                {
                    var diff = center - c;
                    MoveOneChild(state, child, diff);
                }
                leftmost = Math.Min(leftmost, child.Element.Frame.BranchExterior.Left);
                rightmost = Math.Max(rightmost, child.Element.Frame.BranchExterior.Right);
            }

            // update branch boundary
            level.Boundary.ReloadFromBranch(level.BranchRoot);

            return new Dimensions(leftmost, rightmost);
        }
    }
}