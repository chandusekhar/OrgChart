﻿using System;
using Staffer.OrgChart.Annotations;

namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// Called when boundary is updated.
    /// </summary>
    public class BoundaryChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Current layout state.
        /// </summary>
        public readonly LayoutState State;

        /// <summary>
        /// Current layout level.
        /// </summary>
        public readonly LayoutState.LayoutLevel LayoutLevel;

        /// <summary>
        /// The boundary whose state has been changed.
        /// </summary>
        public readonly Boundary Boundary;

        /// <summary>
        /// Ctr.
        /// </summary>
        public BoundaryChangedEventArgs([NotNull]Boundary boundary, [NotNull]LayoutState.LayoutLevel layoutLevel, [NotNull]LayoutState state)
        {
            Boundary = boundary;
            LayoutLevel = layoutLevel;
            State = state;
        }
    }

    /// <summary>
    /// Called when boundary is updated.
    /// </summary>
    public class LayoutStateOperationChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Current layout state.
        /// </summary>
        public readonly LayoutState State;

        /// <summary>
        /// Ctr.
        /// </summary>
        public LayoutStateOperationChangedEventArgs([NotNull]LayoutState state)
        {
            State = state;
        }
    }
}