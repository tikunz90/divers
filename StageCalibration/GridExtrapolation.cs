using System;

public enum ExtrapolationMethod
{
    LinearEdgeSlope // uses first two rows/cols to form slope at boundary
}

public static class StageCalibrationGridExtrapolation
{
    /// <summary>
    /// Returns a new grid that has extra rows/cols added around the original grid.
    /// Example: addTopRows=6 will add 6 rows with Y below Y[0] by multiples of Dy.
    /// Offsets are extrapolated using a chosen method.
    /// </summary>
    public static StageCalibrationGrid Expand(
        StageCalibrationGrid g,
        int addLeftCols = 0,
        int addRightCols = 0,
        int addTopRows = 0,
        int addBottomRows = 0,
        ExtrapolationMethod method = ExtrapolationMethod.LinearEdgeSlope)
    {
        if (addLeftCols < 0 || addRightCols < 0 || addTopRows < 0 || addBottomRows < 0)
            throw new ArgumentOutOfRangeException("Expansion counts must be >= 0.");

        int newCols = g.Columns + addLeftCols + addRightCols;
        int newRows = g.Rows + addTopRows + addBottomRows;

        // Build expanded coordinate arrays (uniform)
        double[] newX = new double[newCols];
        double[] newY = new double[newRows];

        for (int ix = 0; ix < newCols; ix++)
            newX[ix] = g.X[0] + (ix - addLeftCols) * g.Dx;

        for (int iy = 0; iy < newRows; iy++)
            newY[iy] = g.Y[0] + (iy - addTopRows) * g.Dy;

        var newOffsets = new Offset2D[newRows, newCols];

        // Helper: fetch original offset with clamp (we'll use for interior copy)
        Offset2D GetOrig(int oy, int ox) => g.NodeOffsets[oy, ox];

        // 1) Copy original interior block into the expanded grid
        for (int oy = 0; oy < g.Rows; oy++)
        for (int ox = 0; ox < g.Columns; ox++)
        {
            int ny = oy + addTopRows;
            int nx = ox + addLeftCols;
            newOffsets[ny, nx] = GetOrig(oy, ox);
        }

        // 2) Extrapolate top/bottom bands (row-wise in Y) for columns that correspond to original X range
        if (method == ExtrapolationMethod.LinearEdgeSlope)
        {
            // Top extrapolation
            for (int k = 1; k <= addTopRows; k++)
            {
                int ny = addTopRows - k; // rows above original
                int oy0 = 0;
                int oy1 = 1;

                for (int ox = 0; ox < g.Columns; ox++)
                {
                    int nx = ox + addLeftCols;
                    var o0 = g.NodeOffsets[oy0, ox];
                    var o1 = g.NodeOffsets[oy1, ox];
                    var slope = new Offset2D(
                        (o1.Xoff - o0.Xoff) / g.Dy,
                        (o1.Yoff - o0.Yoff) / g.Dy
                    );

                    double dist = k * g.Dy;
                    newOffsets[ny, nx] = new Offset2D(
                        o0.Xoff - slope.Xoff * dist,
                        o0.Yoff - slope.Yoff * dist
                    );
                }
            }

            // Bottom extrapolation
            for (int k = 1; k <= addBottomRows; k++)
            {
                int ny = addTopRows + (g.Rows - 1) + k; // rows below original
                int oyN = g.Rows - 1;
                int oyN1 = g.Rows - 2;

                for (int ox = 0; ox < g.Columns; ox++)
                {
                    int nx = ox + addLeftCols;
                    var oN = g.NodeOffsets[oyN, ox];
                    var oN1 = g.NodeOffsets[oyN1, ox];
                    var slope = new Offset2D(
                        (oN.Xoff - oN1.Xoff) / g.Dy,
                        (oN.Yoff - oN1.Yoff) / g.Dy
                    );

                    double dist = k * g.Dy;
                    newOffsets[ny, nx] = new Offset2D(
                        oN.Xoff + slope.Xoff * dist,
                        oN.Yoff + slope.Yoff * dist
                    );
                }
            }
        }

        // 3) Extrapolate left/right bands in X for ALL rows in expanded grid
        // For stability: do it after top/bottom so corners also get filled.
        if (method == ExtrapolationMethod.LinearEdgeSlope)
        {
            for (int ny = 0; ny < newRows; ny++)
            {
                // We need a "reference band" inside original X range at this ny.
                // If ny maps outside original Y, we already created those rows for original columns (step 2).
                // So we can use newOffsets[ny, addLeftCols + ox] as the interior values.

                int nx0 = addLeftCols;                 // first original column in expanded grid
                int nx1 = addLeftCols + 1;             // second original column
                int nxN = addLeftCols + (g.Columns - 1);
                int nxN1 = addLeftCols + (g.Columns - 2);

                // Left
                for (int k = 1; k <= addLeftCols; k++)
                {
                    int nx = addLeftCols - k;
                    var o0 = newOffsets[ny, nx0];
                    var o1 = newOffsets[ny, nx1];
                    var slope = new Offset2D(
                        (o1.Xoff - o0.Xoff) / g.Dx,
                        (o1.Yoff - o0.Yoff) / g.Dx
                    );

                    double dist = k * g.Dx;
                    newOffsets[ny, nx] = new Offset2D(
                        o0.Xoff - slope.Xoff * dist,
                        o0.Yoff - slope.Yoff * dist
                    );
                }

                // Right
                for (int k = 1; k <= addRightCols; k++)
                {
                    int nx = nxN + k;
                    var oN = newOffsets[ny, nxN];
                    var oN1 = newOffsets[ny, nxN1];
                    var slope = new Offset2D(
                        (oN.Xoff - oN1.Xoff) / g.Dx,
                        (oN.Yoff - oN1.Yoff) / g.Dx
                    );

                    double dist = k * g.Dx;
                    newOffsets[ny, nx] = new Offset2D(
                        oN.Xoff + slope.Xoff * dist,
                        oN.Yoff + slope.Yoff * dist
                    );
                }
            }
        }

        // Create a new grid object.
        // Note: TotalWidth/Height in header are descriptive; for expanded grids, recompute nominal span.
        double newTotalW = (newCols - 1) * g.Dx;
        double newTotalH = (newRows - 1) * g.Dy;

        return CreateFromArrays(newCols, newRows, newTotalW, newTotalH, newX, newY, newOffsets);
    }

    // Helper factory: avoids re-parsing; keeps StageCalibrationGrid mostly immutable.
    private static StageCalibrationGrid CreateFromArrays(
        int cols, int rows, double totalW, double totalH,
        double[] x, double[] y, Offset2D[,] offsets)
    {
        // Use reflection-free approach: place this method inside StageCalibrationGrid if you prefer.
        return (StageCalibrationGrid)Activator.CreateInstance(
            typeof(StageCalibrationGrid),
            nonPublic: true,
            args: new object[] { cols, rows, totalW, totalH, x, y, offsets }
        )!;
    }
}
