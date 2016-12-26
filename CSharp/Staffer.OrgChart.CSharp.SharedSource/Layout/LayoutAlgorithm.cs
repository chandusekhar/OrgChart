﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        public static Rect ComputeBranchVisualBoundingRect([NotNull]BoxTree visualTree)
        {
            var result = new Rect();
            var initialized = false;

            BoxTree.TreeNode.IterateParentFirst(visualTree.Roots[0], node =>
            {
                var box = node.Element;

                if (box.AffectsLayout && !box.IsSpecial)
                {
                    if (initialized)
                    {
                        result += new Rect(node.State.TopLeft, node.State.Size);
                    }
                    else
                    {
                        initialized = true;
                        result = new Rect(node.State.TopLeft, node.State.Size);
                    }
                }

                return !box.IsCollapsed;
            });

            return result;
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

            var tree = BoxTree.Build(state.Diagram.Boxes.BoxesById.Values, x => x.Id, x => x.ParentId);

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
                    box.Size = state.BoxSizeFunc(box.DataId);
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

        private static void PreprocessVisualTree([NotNull]LayoutState state, [NotNull]BoxTree visualTree)
        {
            var defaultStrategyId = state.Diagram.LayoutSettings.DefaultLayoutStrategyId;
            var defaultStrategy = state.Diagram.LayoutSettings.RequireDefaultLayoutStrategy();
            var defaultAssistantsStrategyId = state.Diagram.LayoutSettings.DefaultAssistantLayoutStrategyId;
            var defaultAssistantsStrategy = state.Diagram.LayoutSettings.RequireDefaultAssistantLayoutStrategy();

            visualTree.IterateParentFirst(node =>
            {
                LayoutStrategyBase found = null;
                if (node.ParentNode?.AssistantsRoot == node)
                {
                    // find and associate assistant layout strategy in effect for this node
                    var parent = node;
                    while (parent != null)
                    {
                        if (parent.Element.AssistantLayoutStrategyId != null)
                        {
                            // can we inherit it from previous level?
                            found = state.Diagram.LayoutSettings.LayoutStrategies[parent.Element.AssistantLayoutStrategyId];
                            break;
                        }
                        parent = parent.ParentNode;
                    }

                    if (found == null)
                    {
                        found = defaultAssistantsStrategy;
                    }
                }
                else
                {
                    // find and associate layout strategy in effect for this node
                    var parent = node;
                    while (parent != null)
                    {
                        if (parent.Element.LayoutStrategyId != null)
                        {
                            // can we inherit it from previous level?
                            found = state.Diagram.LayoutSettings.LayoutStrategies[parent.Element.LayoutStrategyId];
                            break;
                        }
                        parent = parent.ParentNode;
                    }

                    if (found == null)
                    {
                        found = defaultStrategy;
                    }
                }

                node.State.TopLeft = new Point(0, 0);
                node.State.Size = node.Element.Size;
                node.State.BranchExterior = new Rect(new Point(0, 0), node.Element.Size);

                // now let it pre-allocate special boxes etc
                node.State.EffectiveLayoutStrategy = found;
                node.State.RequireLayoutStrategy().PreProcessThisNode(state, node);

                return !node.Element.IsCollapsed && node.ChildCount > 0 || node.AssistantsRoot != null;
            });
        }

        /// <summary>
        /// Re-entrant layout algorithm,
        /// </summary>
        public static void HorizontalLayout([NotNull]LayoutState state, [NotNull]BoxTree.TreeNode branchRoot)
        {
            if (!branchRoot.Element.AffectsLayout)
            {
                throw new InvalidOperationException($"Branch root {branchRoot.Element.Id} does not affect layout");
            }

            var level = state.PushLayoutLevel(branchRoot);
            try
            {
                if (branchRoot.Level == 0 || 
                    (branchRoot.State.NumberOfSiblings > 0 || branchRoot.AssistantsRoot != null) 
                    && !branchRoot.Element.IsCollapsed)
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
        public static void VerticalLayout([NotNull]LayoutState state, [NotNull]BoxTree.TreeNode branchRoot)
        {
            if (!branchRoot.Element.AffectsLayout)
            {
                throw new InvalidOperationException($"Branch root {branchRoot.Element.Id} does not affect layout");
            }

            var level = state.PushLayoutLevel(branchRoot);
            try
            {
                if (branchRoot.Level == 0 ||
                    (branchRoot.State.NumberOfSiblings > 0 || branchRoot.AssistantsRoot != null)
                    && !branchRoot.Element.IsCollapsed)
                {
                    branchRoot.State.RequireLayoutStrategy().ApplyVerticalLayout(state, level);
                }
            }
            finally
            {
                state.PopLayoutLevel();
            }
        }

        private static void RouteConnectors([NotNull]LayoutState state, [NotNull]BoxTree visualTree)
        {
            visualTree.IterateParentFirst(node =>
            {
                if (node.Element.IsCollapsed || node.State.NumberOfSiblings == 0 && node.AssistantsRoot == null)
                {
                    return false;
                }

                if (node.Level == 0)
                {
                    return true;
                }

                if (!node.Element.IsSpecial || node.IsAssistantRoot)
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

            Func<Tree<int, Box, NodeLayoutInfo>.TreeNode, bool> action = node =>
            {
                if (node.Element.AffectsLayout)
                {
                    node.State.TopLeft = node.State.TopLeft.MoveH(offset);
                    node.State.BranchExterior = node.State.BranchExterior.MoveH(offset);
                }
                return true;
            };

            //if (layoutLevel.BranchRoot.AssistantsRoot != null)
            {
                //BoxTree.TreeNode.IterateChildFirst(layoutLevel.BranchRoot.AssistantsRoot, action);
            }

            foreach (var child in children)
            {
                BoxTree.TreeNode.IterateChildFirst(child, action);
            }

            layoutLevel.Boundary.ReloadFromBranch(layoutLevel.BranchRoot);
            layoutLevel.BranchRoot.State.BranchExterior = layoutLevel.Boundary.BoundingRect;
        }

        /// <summary>
        /// Moves a given branch horizontally, except its root box.
        /// Also updates branch exterior rects.
        /// Unlike <see cref="MoveChildrenOnly"/> and <see cref="MoveBranch"/>, does NOT update the boundary.
        /// </summary>
        /// <remarks>DOES NOT update branch boundary! Must call <see cref="Boundary.ReloadFromBranch"/> after batch of updates is complete</remarks>
        public static void MoveOneChild([NotNull]LayoutState state, [NotNull]BoxTree.TreeNode root, double offset)
        {
            BoxTree.TreeNode.IterateChildFirst(root,
                node =>
                {
                    if (node.Element.AffectsLayout)
                    {
                        node.State.TopLeft = node.State.TopLeft.MoveH(offset);
                        node.State.BranchExterior = node.State.BranchExterior.MoveH(offset);
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
            layoutLevel.BranchRoot.State.BranchExterior = layoutLevel.Boundary.BoundingRect;
        }

        /// <summary>
        /// Vertically aligns a subset of child nodes, presumably located one above another.
        /// All children must belong to the current layout level's root.
        /// Returns leftmost and rightmost boundaries of all branches in the <paramref name="subset"/>, after alignment.
        /// </summary>
        public static Dimensions AlignHorizontalCenters(
            [NotNull]LayoutState state, 
            [NotNull]LayoutState.LayoutLevel level,
            [NotNull]IEnumerable<BoxTree.TreeNode> subset)
        {
            // compute the rightmost center in the column
            var center = double.MinValue;
            foreach (var child in subset)
            {
                var c = child.State.CenterH;
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
                var frame = child.State;
                var c = frame.CenterH;
                if (!c.IsEqual(center))
                {
                    var diff = center - c;
                    MoveOneChild(state, child, diff);
                }
                leftmost = Math.Min(leftmost, child.State.BranchExterior.Left);
                rightmost = Math.Max(rightmost, child.State.BranchExterior.Right);
            }

            // update branch boundary
            level.Boundary.ReloadFromBranch(level.BranchRoot);

            return new Dimensions(leftmost, rightmost);
        }
        
        /// <summary>
        /// Resets content to start a fresh layout.
        /// Does not modify size of the <see cref="NodeLayoutInfo.TopLeft"/>.
        /// </summary>
        public static void ResetLayout([NotNull]this NodeLayoutInfo state)
        {
            state.TopLeft = new Point();
            state.BranchExterior = new Rect(state.TopLeft, state.Size);
            state.Connector = null;
            state.SiblingsRowV = Dimensions.MinMax();
        }

        /// <summary>
        /// Copies vertical and horionztal measurement data from <paramref name="other"/> frame.
        /// Does not copy <see cref="Connector"/>.
        /// </summary>
        public static void CopyExteriorFrom([NotNull]this NodeLayoutInfo state, [NotNull]NodeLayoutInfo other)
        {
            state.TopLeft = other.TopLeft;
            state.Size = other.Size;
            state.BranchExterior = other.BranchExterior;
            state.SiblingsRowV = other.SiblingsRowV;
        }

        /// <summary>
        /// <c>true</c> if specified <paramref name="value"/> is equal to <see cref="double.MinValue"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMinValue(this double value)
        {
            return value <= double.MinValue + double.Epsilon;
        }

        /// <summary>
        /// <c>true</c> if specified <paramref name="value"/> is equal to <see cref="double.MinValue"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMaxValue(this double value)
        {
            return value >= double.MaxValue - double.Epsilon;
        }

        /// <summary>
        /// <c>true</c> if specified <paramref name="value"/> is equal to <see cref="double.MinValue"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(this double value)
        {
            return value <= double.Epsilon && value >= -double.Epsilon;
        }

        /// <summary>
        /// <c>true</c> if specified <paramref name="value"/> is equal to <see cref="double.MinValue"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEqual(this double value, double other)
        {
            return Math.Abs(value - other) <= double.Epsilon;
        }
    }
}