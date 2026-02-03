using System;

namespace CalibRecalibrator
{
    /// <summary>
    /// Performs bilinear interpolation on measured residual grid.
    /// </summary>
    public class BilinearInterpolator
    {
        private readonly MeasuredDataParser measuredData;

        public BilinearInterpolator(MeasuredDataParser measuredData)
        {
            this.measuredData = measuredData;
        }

        /// <summary>
        /// Interpolates residual value at point (x, y) in mm.
        /// Returns 0 if point is outside the measured grid bounds.
        /// </summary>
        public double Interpolate(double x, double y, double[,] residuals)
        {
            double halfWidth = measuredData.Width / 2.0;
            double halfHeight = measuredData.Height / 2.0;

            // Return 0 for points outside the measured rectangle
            if (x < -halfWidth || x > halfWidth || y < -halfHeight || y > halfHeight)
                return 0.0;

            double px = measuredData.Width / (measuredData.Cols - 1);
            double py = measuredData.Height / (measuredData.Rows - 1);

            // Find the grid cell containing point (x, y)
            // col index from x position
            double colExact = (x + halfWidth) / px;
            int col0 = (int)Math.Floor(colExact);
            int col1 = col0 + 1;

            // row index from y position
            double rowExact = (y + halfHeight) / py;
            int row0 = (int)Math.Floor(rowExact);
            int row1 = row0 + 1;

            // Clamp to grid bounds
            col0 = Math.Max(0, Math.Min(col0, measuredData.Cols - 1));
            col1 = Math.Max(0, Math.Min(col1, measuredData.Cols - 1));
            row0 = Math.Max(0, Math.Min(row0, measuredData.Rows - 1));
            row1 = Math.Max(0, Math.Min(row1, measuredData.Rows - 1));

            // If we're exactly on a grid point, return that value
            if (col0 == col1 && row0 == row1)
                return residuals[row0, col0];

            // Get the four corner values
            double v00 = residuals[row0, col0];
            double v01 = residuals[row0, col1];
            double v10 = residuals[row1, col0];
            double v11 = residuals[row1, col1];

            // Calculate interpolation weights
            double wx = (col0 == col1) ? 0.0 : (colExact - col0) / (col1 - col0);
            double wy = (row0 == row1) ? 0.0 : (rowExact - row0) / (row1 - row0);

            // Bilinear interpolation
            double v0 = v00 * (1 - wx) + v01 * wx;
            double v1 = v10 * (1 - wx) + v11 * wx;
            double result = v0 * (1 - wy) + v1 * wy;

            return result;
        }
    }
}
