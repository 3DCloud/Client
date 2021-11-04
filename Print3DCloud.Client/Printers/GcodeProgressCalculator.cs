using System;
using System.Collections.Generic;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Helper class that calculates a print's estimated time left.
    /// </summary>
    public class GcodeProgressCalculator
    {
        private readonly int totalEstimatedTime;
        private readonly List<ProgressTimeStep> steps;

        private double lastElapsed;
        private double durationFactor = 1;
        private int currentStep;

        /// <summary>
        /// Initializes a new instance of the <see cref="GcodeProgressCalculator"/> class.
        /// </summary>
        /// <param name="totalEstimatedTime">The total estimated time the print will take.</param>
        /// <param name="steps">The print progress steps.</param>
        public GcodeProgressCalculator(int totalEstimatedTime, List<ProgressTimeStep> steps)
        {
            this.totalEstimatedTime = totalEstimatedTime;
            this.steps = steps;
        }

        /// <summary>
        /// Get the estimated amount of time left for a print.
        /// </summary>
        /// <param name="elapsedSeconds">The seconds elapsed since the beginning of the print.</param>
        /// <param name="bytePosition">The current position in the file.</param>
        /// <returns>A <see cref="TimeEstimate"/> containing the estimate.</returns>
        public TimeEstimate GetEstimate(double elapsedSeconds, long bytePosition)
        {
            if (this.steps.Count == 0)
            {
                return new TimeEstimate(0, 0);
            }

            if (bytePosition <= this.steps[0].BytePosition)
            {
                return new TimeEstimate(this.steps[0].TimeRemaining, 0);
            }

            if (bytePosition >= this.steps[^1].BytePosition)
            {
                return new TimeEstimate(0, 1);
            }

            while (this.steps[this.currentStep + 1].BytePosition <= bytePosition)
            {
                ++this.currentStep;
            }

            ProgressTimeStep current = this.steps[this.currentStep];
            ProgressTimeStep next = this.steps[this.currentStep + 1];

            double stepProgress = (double)(bytePosition - current.BytePosition) / (next.BytePosition - current.BytePosition);
            double rawEstimate = Lerp(current.TimeRemaining, next.TimeRemaining, stepProgress);
            double progress = 1 - rawEstimate / this.totalEstimatedTime;
            double fac = (elapsedSeconds - this.lastElapsed) * 0.001;
            this.durationFactor = this.durationFactor * (1 - fac) + fac * elapsedSeconds / (this.totalEstimatedTime - rawEstimate);
            int adjustedEstimate = (int)Math.Round(rawEstimate * this.durationFactor);
            this.lastElapsed = elapsedSeconds;

            return new TimeEstimate(
                adjustedEstimate,
                progress);
        }

        /// <summary>
        /// Linearly interpolate between <paramref name="from"/> and <paramref name="to"/> by the interpolant <paramref name="t"/>.
        /// </summary>
        /// <param name="from">Start value, returned when t = 0.</param>
        /// <param name="to">End value, returned when t = 1.</param>
        /// <param name="t">Value used to interpolate between a and b.</param>
        /// <returns>Interpolated value, equal to <paramref name="from"/> + (<paramref name="to"/> - <paramref name="from"/>) * <paramref name="t"/>.</returns>
        private static double Lerp(double from, double to, double t)
        {
            return from + (to - from) * t;
        }
    }
}