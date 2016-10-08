﻿using System;
using Staffer.OrgChart.Annotations;

namespace Staffer.OrgChart.Layout
{
    /// <summary>
    /// A visual connector between two or more objects.
    /// </summary>
    public class Connector
    {
        /// <summary>
        /// Ctr.
        /// </summary>
        public Connector([NotNull]Edge[] segments)
        {
            if (segments.Length == 0)
            {
                throw new ArgumentException("Need at least one segment", nameof(Segments));
            }
            Segments = segments;
        }

        /// <summary>
        /// All individual segments of a connector, sorted from beginning to end.
        /// </summary>
        [NotNull]
        public Edge[] Segments { get; }
    }
}