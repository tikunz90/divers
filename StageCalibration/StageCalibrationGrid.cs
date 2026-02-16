using System;

public enum InterpolationMethod
{
    Bilinear,
    BicubicCatmullRom,
    CubicSplineSeparable
}

public enum OutOfGridPolicy
{
    // Legacy behaviors (still useful)
    Clamp,
    ReturnNaN,
    Throw,

    // Extrapolation behaviors
    ExtrapolateLinearEdgeSlope,
    ExtrapolateLinearRegressionSlope,
    ExtrapolatePlaneFit,
    ExtrapolateSpline // spline evaluation outside knot range
}

public enum SplineBoundary
{
    Natural,
    Clamped
}

public enum PlaneFitEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public readonly record struct Offset2D(double Xoff, double Yoff);

public sealed class StageCalibrationGrid
{
    public int Columns { get; }
    public int Rows { get; }
    public double[] X { get; }
    public double[] Y { get; }
    public double Dx { get; }
    public double Dy { get; }
    public Offset2D[,] NodeOffsets { get; }

    // Keep constructor internal so helper classes can create expanded grids without reflection.
    internal StageCalibrationGrid(int cols, int rows, double[] x, double[] y, double dx, double dy, Offset2D[,] nodeOffsets)
    {
        Columns = cols;
        Rows = rows;
        X = x;
        Y = y;
        Dx = dx;
        Dy = dy;
        NodeOffsets = nodeOffsets;
    }

    // ---------------------------
    // Public API
    // ---------------------------

    public Offset2D GetOffset(
        double xPosMm,
        double yPosMm,
        InterpolationMethod interp = InterpolationMethod.Bilinear,
        OutOfGridPolicy outPolicy = OutOfGridPolicy.Clamp,
        ExtrapolationSettings? extrapSettings = null,
        SplineSettings? splineSettings = null)
    {
        extrapSettings ??= ExtrapolationSettings.Default;
        splineSettings ??= SplineSettings.Default;

        bool inside = (xPosMm >= X[0] && xPosMm <= X[^1] && yPosMm >= Y[0] && yPosMm <= Y[^1]);
        if (!inside)
        {
            return outPolicy switch
            {
                OutOfGridPolicy.Throw => throw new ArgumentOutOfRangeException($"Position (X={xPosMm},Y={yPosMm}) outside grid."),
                OutOfGridPolicy.ReturnNaN => new Offset2D(double.NaN, double.NaN),
                OutOfGridPolicy.Clamp => GetOffset_Inside(
                    Math.Clamp(xPosMm, X[0], X[^1]),
                    Math.Clamp(yPosMm, Y[0], Y[^1]),
                    interp, splineSettings),

                OutOfGridPolicy.ExtrapolateLinearEdgeSlope =>
                    Extrapolate_LinearEdgeSlope(xPosMm, yPosMm, interp, extrapSettings, splineSettings),

                OutOfGridPolicy.ExtrapolateLinearRegressionSlope =>
                    Extrapolate_LinearRegressionSlope(xPosMm, yPosMm, interp, extrapSettings, splineSettings),

                OutOfGridPolicy.ExtrapolatePlaneFit =>
                    Extrapolate_PlaneFit(xPosMm, yPosMm, interp, extrapSettings, splineSettings),

                OutOfGridPolicy.ExtrapolateSpline =>
                    Extrapolate_Spline(xPosMm, yPosMm, interp, extrapSettings, splineSettings),

                _ => throw new NotSupportedException(outPolicy.ToString())
            };
        }

        return GetOffset_Inside(xPosMm, yPosMm, interp, splineSettings);
    }

    /// <summary>
    /// Materialize an expanded grid by adding rows/cols and filling them via chosen extrapolation policy.
    /// Use outPolicy = Extrapolate* for fill generation.
    /// </summary>
    public StageCalibrationGrid Expand(
        int addLeftCols = 0,
        int addRightCols = 0,
        int addTopRows = 0,
        int addBottomRows = 0,
        OutOfGridPolicy fillPolicy = OutOfGridPolicy.ExtrapolateLinearRegressionSlope,
        ExtrapolationSettings? extrapSettings = null,
        SplineSettings? splineSettings = null,
        InterpolationMethod interpForFill = InterpolationMethod.Bilinear)
    {
        extrapSettings ??= ExtrapolationSettings.Default;
        splineSettings ??= SplineSettings.Default;

        if (addLeftCols < 0 || addRightCols < 0 || addTopRows < 0 || addBottomRows < 0)
            throw new ArgumentOutOfRangeException("Expansion counts must be >= 0.");

        int newCols = Columns + addLeftCols + addRightCols;
        int newRows = Rows + addTopRows + addBottomRows;

        double[] newX = new double[newCols];
        double[] newY = new double[newRows];

        for (int ix = 0; ix < newCols; ix++)
            newX[ix] = X[0] + (ix - addLeftCols) * Dx;
        for (int iy = 0; iy < newRows; iy++)
            newY[iy] = Y[0] + (iy - addTopRows) * Dy;

        var newOffsets = new Offset2D[newRows, newCols];

        // Fill every node by querying original grid with extrapolation policy.
        for (int iy = 0; iy < newRows; iy++)
        for (int ix = 0; ix < newCols; ix++)
        {
            double qx = newX[ix];
            double qy = newY[iy];
            newOffsets[iy, ix] = GetOffset(qx, qy, interpForFill, fillPolicy, extrapSettings, splineSettings);
        }

        return new StageCalibrationGrid(newCols, newRows, newX, newY, Dx, Dy, newOffsets);
    }

    // ---------------------------
    // Settings objects
    // ---------------------------

    public sealed record ExtrapolationSettings(
        int RegressionBandPoints = 5,          // K points for regression slope/plane fit band thickness
        double MaxExtrapolationDistanceMm = double.PositiveInfinity, // cap distance
        double MaxSlopeMmPerMm = double.PositiveInfinity            // cap slope magnitude
    )
    {
        public static readonly ExtrapolationSettings Default = new();
    }

    public sealed record SplineSettings(
        SplineBoundary Boundary = SplineBoundary.Clamped,
        // Derivatives in mm/mm. If null and Clamped: auto-estimate from edge finite difference.
        double? DxStart_Xoff = null, double? DxEnd_Xoff = null,
        double? DxStart_Yoff = null, double? DxEnd_Yoff = null,
        double? DyStart_Xoff = null, double? DyEnd_Xoff = null,
        double? DyStart_Yoff = null, double? DyEnd_Yoff = null
    )
    {
        public static readonly SplineSettings Default = new();
    }

    // ---------------------------
    // Inside-grid interpolation
    // ---------------------------

    private Offset2D GetOffset_Inside(double x, double y, InterpolationMethod interp, SplineSettings spline)
    {
        // Map to cell
        int ix0 = FindLowerIndex(X, x);
        int iy0 = FindLowerIndex(Y, y);
        ix0 = Math.Clamp(ix0, 0, Columns - 2);
        iy0 = Math.Clamp(iy0, 0, Rows - 2);

        double tx = (x - X[ix0]) / (X[ix0 + 1] - X[ix0]);
        double ty = (y - Y[iy0]) / (Y[iy0 + 1] - Y[iy0]);
        tx = Math.Clamp(tx, 0.0, 1.0);
        ty = Math.Clamp(ty, 0.0, 1.0);

        return interp switch
        {
            InterpolationMethod.Bilinear => Bilinear(ix0, iy0, tx, ty),
            InterpolationMethod.BicubicCatmullRom => BicubicCatmullRom(ix0, iy0, tx, ty, clampNeighborhood: true),
            InterpolationMethod.CubicSplineSeparable => SplineSeparable(x, y, spline, allowExtrapolation: false),
            _ => throw new NotSupportedException(interp.ToString())
        };
    }

    private Offset2D Bilinear(int ix0, int iy0, double tx, double ty)
    {
        var o00 = NodeOffsets[iy0, ix0];
        var o10 = NodeOffsets[iy0, ix0 + 1];
        var o01 = NodeOffsets[iy0 + 1, ix0];
        var o11 = NodeOffsets[iy0 + 1, ix0 + 1];

        double xoff =
            (1 - tx) * (1 - ty) * o00.Xoff +
            tx * (1 - ty) * o10.Xoff +
            (1 - tx) * ty * o01.Xoff +
            tx * ty * o11.Xoff;

        double yoff =
            (1 - tx) * (1 - ty) * o00.Yoff +
            tx * (1 - ty) * o10.Yoff +
            (1 - tx) * ty * o01.Yoff +
            tx * ty * o11.Yoff;

        return new Offset2D(xoff, yoff);
    }

    private Offset2D BicubicCatmullRom(int ix0, int iy0, double tx, double ty, bool clampNeighborhood)
    {
        Offset2D Sample(int ix, int iy)
        {
            if (clampNeighborhood)
            {
                ix = Math.Clamp(ix, 0, Columns - 1);
                iy = Math.Clamp(iy, 0, Rows - 1);
                return NodeOffsets[iy, ix];
            }

            if (ix < 0 || ix >= Columns || iy < 0 || iy >= Rows)
                return new Offset2D(double.NaN, double.NaN);

            return NodeOffsets[iy, ix];
        }

        double[] rowX = new double[4];
        double[] rowY = new double[4];

        for (int m = -1; m <= 2; m++)
        {
            var p0 = Sample(ix0 - 1, iy0 + m);
            var p1 = Sample(ix0 + 0, iy0 + m);
            var p2 = Sample(ix0 + 1, iy0 + m);
            var p3 = Sample(ix0 + 2, iy0 + m);

            rowX[m + 1] = CatmullRom(p0.Xoff, p1.Xoff, p2.Xoff, p3.Xoff, tx);
            rowY[m + 1] = CatmullRom(p0.Yoff, p1.Yoff, p2.Yoff, p3.Yoff, tx);
        }

        double xoff = CatmullRom(rowX[0], rowX[1], rowX[2], rowX[3], ty);
        double yoff = CatmullRom(rowY[0], rowY[1], rowY[2], rowY[3], ty);

        return new Offset2D(xoff, yoff);
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    // ---------------------------
    // Extrapolation implementations
    // ---------------------------

    // A) Linear edge slope: use bilinear inside on the clamped edge, then extend along outward normal using edge slope.
    private Offset2D Extrapolate_LinearEdgeSlope(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        // Cap distance
        (x, y) = CapDistance(x, y, ex);

        // If both x and y are out, do a two-step: bring to edge point, extrapolate in y then in x (order chosen).
        // Simpler and stable enough for calibration usage.
        if (y < Y[0]) return ExtrapolateFromTop_EdgeSlope(x, y, interp, ex, spline);
        if (y > Y[^1]) return ExtrapolateFromBottom_EdgeSlope(x, y, interp, ex, spline);
        if (x < X[0]) return ExtrapolateFromLeft_EdgeSlope(x, y, interp, ex, spline);
        return ExtrapolateFromRight_EdgeSlope(x, y, interp, ex, spline);
    }

    private Offset2D ExtrapolateFromTop_EdgeSlope(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double yEdge = Y[0];
        double dist = y - yEdge; // negative
        double y1 = Y[1];

        // Evaluate edge offset at (x, yEdge) and (x, y1) using inside-grid interpolation (clamp x only)
        var o0 = GetOffset_Inside(Math.Clamp(x, X[0], X[^1]), yEdge, interp, spline);
        var o1 = GetOffset_Inside(Math.Clamp(x, X[0], X[^1]), y1, interp, spline);

        var slope = new Offset2D((o1.Xoff - o0.Xoff) / Dy, (o1.Yoff - o0.Yoff) / Dy);
        slope = CapSlope(slope, ex);

        return new Offset2D(o0.Xoff + slope.Xoff * dist, o0.Yoff + slope.Yoff * dist);
    }

    private Offset2D ExtrapolateFromBottom_EdgeSlope(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double yEdge = Y[^1];
        double dist = y - yEdge; // positive
        double yN1 = Y[^2];

        var oN = GetOffset_Inside(Math.Clamp(x, X[0], X[^1]), yEdge, interp, spline);
        var oN1 = GetOffset_Inside(Math.Clamp(x, X[0], X[^1]), yN1, interp, spline);

        var slope = new Offset2D((oN.Xoff - oN1.Xoff) / Dy, (oN.Yoff - oN1.Yoff) / Dy);
        slope = CapSlope(slope, ex);

        return new Offset2D(oN.Xoff + slope.Xoff * dist, oN.Yoff + slope.Yoff * dist);
    }

    private Offset2D ExtrapolateFromLeft_EdgeSlope(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double xEdge = X[0];
        double dist = x - xEdge; // negative
        double x1 = X[1];

        var o0 = GetOffset_Inside(xEdge, Math.Clamp(y, Y[0], Y[^1]), interp, spline);
        var o1 = GetOffset_Inside(x1, Math.Clamp(y, Y[0], Y[^1]), interp, spline);

        var slope = new Offset2D((o1.Xoff - o0.Xoff) / Dx, (o1.Yoff - o0.Yoff) / Dx);
        slope = CapSlope(slope, ex);

        return new Offset2D(o0.Xoff + slope.Xoff * dist, o0.Yoff + slope.Yoff * dist);
    }

    private Offset2D ExtrapolateFromRight_EdgeSlope(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double xEdge = X[^1];
        double dist = x - xEdge; // positive
        double xN1 = X[^2];

        var oN = GetOffset_Inside(xEdge, Math.Clamp(y, Y[0], Y[^1]), interp, spline);
        var oN1 = GetOffset_Inside(xN1, Math.Clamp(y, Y[0], Y[^1]), interp, spline);

        var slope = new Offset2D((oN.Xoff - oN1.Xoff) / Dx, (oN.Yoff - oN1.Yoff) / Dx);
        slope = CapSlope(slope, ex);

        return new Offset2D(oN.Xoff + slope.Xoff * dist, oN.Yoff + slope.Yoff * dist);
    }

    // B) Linear regression slope (more robust): fit slope over first/last K samples along outward normal.
    private Offset2D Extrapolate_LinearRegressionSlope(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        (x, y) = CapDistance(x, y, ex);

        if (y < Y[0]) return ExtrapolateFromTop_Regression(x, y, interp, ex, spline);
        if (y > Y[^1]) return ExtrapolateFromBottom_Regression(x, y, interp, ex, spline);
        if (x < X[0]) return ExtrapolateFromLeft_Regression(x, y, interp, ex, spline);
        return ExtrapolateFromRight_Regression(x, y, interp, ex, spline);
    }

    private Offset2D ExtrapolateFromTop_Regression(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double yEdge = Y[0];
        double dist = y - yEdge; // negative

        int k = Math.Clamp(ex.RegressionBandPoints, 2, Rows);
        // Fit offset vs y using points y[0..k-1] at fixed x (evaluate each at x using inside interpolation)
        // Linear regression: off = a + b*y => slope b
        var (bX, bY, aX, aY) = FitLineYBand_Top(x, k, interp, spline);
        var slope = CapSlope(new Offset2D(bX, bY), ex);

        var oEdge = new Offset2D(aX + bX * yEdge, aY + bY * yEdge);
        return new Offset2D(oEdge.Xoff + slope.Xoff * dist, oEdge.Yoff + slope.Yoff * dist);
    }

    private Offset2D ExtrapolateFromBottom_Regression(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double yEdge = Y[^1];
        double dist = y - yEdge;

        int k = Math.Clamp(ex.RegressionBandPoints, 2, Rows);
        var (bX, bY, aX, aY) = FitLineYBand_Bottom(x, k, interp, spline);
        var slope = CapSlope(new Offset2D(bX, bY), ex);

        var oEdge = new Offset2D(aX + bX * yEdge, aY + bY * yEdge);
        return new Offset2D(oEdge.Xoff + slope.Xoff * dist, oEdge.Yoff + slope.Yoff * dist);
    }

    private Offset2D ExtrapolateFromLeft_Regression(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double xEdge = X[0];
        double dist = x - xEdge;

        int k = Math.Clamp(ex.RegressionBandPoints, 2, Columns);
        var (bX, bY, aX, aY) = FitLineXBand_Left(y, k, interp, spline);
        var slope = CapSlope(new Offset2D(bX, bY), ex);

        var oEdge = new Offset2D(aX + bX * xEdge, aY + bY * xEdge);
        return new Offset2D(oEdge.Xoff + slope.Xoff * dist, oEdge.Yoff + slope.Yoff * dist);
    }

    private Offset2D ExtrapolateFromRight_Regression(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        double xEdge = X[^1];
        double dist = x - xEdge;

        int k = Math.Clamp(ex.RegressionBandPoints, 2, Columns);
        var (bX, bY, aX, aY) = FitLineXBand_Right(y, k, interp, spline);
        var slope = CapSlope(new Offset2D(bX, bY), ex);

        var oEdge = new Offset2D(aX + bX * xEdge, aY + bY * xEdge);
        return new Offset2D(oEdge.Xoff + slope.Xoff * dist, oEdge.Yoff + slope.Yoff * dist);
    }

    // C) Plane fit on boundary band: fit off(x,y) = a + b*x + c*y on a band near the relevant edge.
    private Offset2D Extrapolate_PlaneFit(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        (x, y) = CapDistance(x, y, ex);

        PlaneFitEdge edge =
            y < Y[0] ? PlaneFitEdge.Top :
            y > Y[^1] ? PlaneFitEdge.Bottom :
            x < X[0] ? PlaneFitEdge.Left :
            PlaneFitEdge.Right;

        // Fit plane separately for Xoff and Yoff using node samples in the band.
        int band = Math.Clamp(ex.RegressionBandPoints, 2, Math.Max(Rows, Columns));

        var planeX = FitPlane(edge, band, useXoff: true);
        var planeY = FitPlane(edge, band, useXoff: false);

        return new Offset2D(
            planeX.Eval(x, y),
            planeY.Eval(x, y));
    }

    // D) Spline extrapolation: evaluate separable cubic spline outside knots.
    private Offset2D Extrapolate_Spline(double x, double y, InterpolationMethod interp, ExtrapolationSettings ex, SplineSettings spline)
    {
        (x, y) = CapDistance(x, y, ex);

        // For spline option we ignore bilinear/bicubic and use spline engine (interp parameter kept for symmetry).
        // You can still pass interp=CubicSplineSeparable explicitly.
        return SplineSeparable(x, y, spline, allowExtrapolation: true);
    }

    // ---------------------------
    // Helpers: slope caps / distance caps
    // ---------------------------

    private (double x, double y) CapDistance(double x, double y, ExtrapolationSettings ex)
    {
        if (double.IsPositiveInfinity(ex.MaxExtrapolationDistanceMm))
            return (x, y);

        double cx = Math.Clamp(x, X[0] - ex.MaxExtrapolationDistanceMm, X[^1] + ex.MaxExtrapolationDistanceMm);
        double cy = Math.Clamp(y, Y[0] - ex.MaxExtrapolationDistanceMm, Y[^1] + ex.MaxExtrapolationDistanceMm);
        return (cx, cy);
    }

    private static Offset2D CapSlope(Offset2D slope, ExtrapolationSettings ex)
    {
        if (double.IsPositiveInfinity(ex.MaxSlopeMmPerMm))
            return slope;

        double mag = Math.Sqrt(slope.Xoff * slope.Xoff + slope.Yoff * slope.Yoff);
        if (mag <= ex.MaxSlopeMmPerMm) return slope;
        if (mag == 0) return slope;

        double s = ex.MaxSlopeMmPerMm / mag;
        return new Offset2D(slope.Xoff * s, slope.Yoff * s);
    }

    // ---------------------------
    // Regression fitting (line)
    // ---------------------------

    private (double bX, double bY, double aX, double aY) FitLineYBand_Top(double x, int k, InterpolationMethod interp, SplineSettings spline)
    {
        // Fit vs Y using y = Y[i], i=0..k-1
        double sumY = 0, sumYY = 0;
        double sumFx = 0, sumFY = 0;
        double sumYFx = 0, sumYFY = 0;

        double xc = Math.Clamp(x, X[0], X[^1]);

        for (int i = 0; i < k; i++)
        {
            double yy = Y[i];
            var off = GetOffset_Inside(xc, yy, interp, spline);

            sumY += yy;
            sumYY += yy * yy;
            sumFx += off.Xoff;
            sumFY += off.Yoff;
            sumYFx += yy * off.Xoff;
            sumYFY += yy * off.Yoff;
        }

        return SolveLine(sumY, sumYY, sumFx, sumYFx, sumFY, sumYFY, k);
    }

    private (double bX, double bY, double aX, double aY) FitLineYBand_Bottom(double x, int k, InterpolationMethod interp, SplineSettings spline)
    {
        double sumY = 0, sumYY = 0;
        double sumFx = 0, sumFY = 0;
        double sumYFx = 0, sumYFY = 0;

        double xc = Math.Clamp(x, X[0], X[^1]);

        for (int t = 0; t < k; t++)
        {
            int i = (Rows - 1) - t;
            double yy = Y[i];
            var off = GetOffset_Inside(xc, yy, interp, spline);

            sumY += yy;
            sumYY += yy * yy;
            sumFx += off.Xoff;
            sumFY += off.Yoff;
            sumYFx += yy * off.Xoff;
            sumYFY += yy * off.Yoff;
        }

        return SolveLine(sumY, sumYY, sumFx, sumYFx, sumFY, sumYFY, k);
    }

    private (double bX, double bY, double aX, double aY) FitLineXBand_Left(double y, int k, InterpolationMethod interp, SplineSettings spline)
    {
        double sumX = 0, sumXX = 0;
        double sumFx = 0, sumFY = 0;
        double sumXFx = 0, sumXFY = 0;

        double yc = Math.Clamp(y, Y[0], Y[^1]);

        for (int i = 0; i < k; i++)
        {
            double xx = X[i];
            var off = GetOffset_Inside(xx, yc, interp, spline);

            sumX += xx;
            sumXX += xx * xx;
            sumFx += off.Xoff;
            sumFY += off.Yoff;
            sumXFx += xx * off.Xoff;
            sumXFY += xx * off.Yoff;
        }

        return SolveLine(sumX, sumXX, sumFx, sumXFx, sumFY, sumXFY, k);
    }

    private (double bX, double bY, double aX, double aY) FitLineXBand_Right(double y, int k, InterpolationMethod interp, SplineSettings spline)
    {
        double sumX = 0, sumXX = 0;
        double sumFx = 0, sumFY = 0;
        double sumXFx = 0, sumXFY = 0;

        double yc = Math.Clamp(y, Y[0], Y[^1]);

        for (int t = 0; t < k; t++)
        {
            int i = (Columns - 1) - t;
            double xx = X[i];
            var off = GetOffset_Inside(xx, yc, interp, spline);

            sumX += xx;
            sumXX += xx * xx;
            sumFx += off.Xoff;
            sumFY += off.Yoff;
            sumXFx += xx * off.Xoff;
            sumXFY += xx * off.Yoff;
        }

        return SolveLine(sumX, sumXX, sumFx, sumXFx, sumFY, sumXFY, k);
    }

    private static (double bX, double bY, double aX, double aY) SolveLine(
        double sumT, double sumTT,
        double sumFx, double sumTFx,
        double sumFy, double sumTFy,
        int n)
    {
        // Fit f = a + b*T (least squares)
        // b = (n*sum(Tf)-sumT*sumf)/(n*sumTT - sumT^2)
        double denom = n * sumTT - sumT * sumT;
        if (Math.Abs(denom) < 1e-18)
        {
            // degenerate -> constant
            return (0, 0, sumFx / n, sumFy / n);
        }

        double bX = (n * sumTFx - sumT * sumFx) / denom;
        double aX = (sumFx - bX * sumT) / n;

        double bY = (n * sumTFy - sumT * sumFy) / denom;
        double aY = (sumFy - bY * sumT) / n;

        return (bX, bY, aX, aY);
    }

    // ---------------------------
    // Plane fit (least squares): f = a + b*x + c*y
    // ---------------------------

    private readonly record struct Plane(double a, double b, double c)
    {
        public double Eval(double x, double y) => a + b * x + c * y;
    }

    private Plane FitPlane(PlaneFitEdge edge, int band, bool useXoff)
    {
        // Build normal equations for least squares:
        // [n   Σx   Σy ] [a] = [Σf]
        // [Σx  Σx2  Σxy] [b]   [Σxf]
        // [Σy  Σxy  Σy2] [c]   [Σyf]
        double n = 0;
        double Sx = 0, Sy = 0, Sxx = 0, Syy = 0, Sxy = 0;
        double Sf = 0, Sxf = 0, Syf = 0;

        void AddSample(int iy, int ix)
        {
            double x = X[ix];
            double y = Y[iy];
            double f = useXoff ? NodeOffsets[iy, ix].Xoff : NodeOffsets[iy, ix].Yoff;

            n += 1;
            Sx += x;
            Sy += y;
            Sxx += x * x;
            Syy += y * y;
            Sxy += x * y;
            Sf += f;
            Sxf += x * f;
            Syf += y * f;
        }

        band = Math.Clamp(band, 1, Math.Max(Rows, Columns));

        switch (edge)
        {
            case PlaneFitEdge.Top:
                for (int iy = 0; iy < Math.Min(Rows, band); iy++)
                for (int ix = 0; ix < Columns; ix++)
                    AddSample(iy, ix);
                break;

            case PlaneFitEdge.Bottom:
                for (int t = 0; t < Math.Min(Rows, band); t++)
                {
                    int iy = (Rows - 1) - t;
                    for (int ix = 0; ix < Columns; ix++) AddSample(iy, ix);
                }
                break;

            case PlaneFitEdge.Left:
                for (int ix = 0; ix < Math.Min(Columns, band); ix++)
                for (int iy = 0; iy < Rows; iy++)
                    AddSample(iy, ix);
                break;

            case PlaneFitEdge.Right:
                for (int t = 0; t < Math.Min(Columns, band); t++)
                {
                    int ix = (Columns - 1) - t;
                    for (int iy = 0; iy < Rows; iy++) AddSample(iy, ix);
                }
                break;
        }

        // Solve 3x3 system via Cramer's rule / Gaussian elimination
        // Matrix:
        // [n   Sx   Sy ]
        // [Sx  Sxx  Sxy]
        // [Sy  Sxy  Syy]
        double[,] A =
        {
            { n,  Sx,  Sy },
            { Sx, Sxx, Sxy },
            { Sy, Sxy, Syy }
        };
        double[] B = { Sf, Sxf, Syf };

        var sol = Solve3x3(A, B);
        return new Plane(sol[0], sol[1], sol[2]);
    }

    private static double[] Solve3x3(double[,] A, double[] b)
    {
        // Simple Gaussian elimination (no pivoting; fine for well-scaled calibration data).
        double[,] m = (double[,])A.Clone();
        double[] x = (double[])b.Clone();

        for (int k = 0; k < 3; k++)
        {
            double piv = m[k, k];
            if (Math.Abs(piv) < 1e-18)
                throw new InvalidOperationException("Singular plane-fit system.");

            for (int j = k; j < 3; j++) m[k, j] /= piv;
            x[k] /= piv;

            for (int i = 0; i < 3; i++)
            {
                if (i == k) continue;
                double f = m[i, k];
                for (int j = k; j < 3; j++) m[i, j] -= f * m[k, j];
                x[i] -= f * x[k];
            }
        }
        return x;
    }

    // ---------------------------
    // Spline (separable) with optional extrapolation
    // ---------------------------

    private Offset2D SplineSeparable(double xPosMm, double yPosMm, SplineSettings s, bool allowExtrapolation)
    {
        // For "allowExtrapolation=false", clamp query to inside.
        if (!allowExtrapolation)
        {
            xPosMm = Math.Clamp(xPosMm, X[0], X[^1]);
            yPosMm = Math.Clamp(yPosMm, Y[0], Y[^1]);
        }

        // Auto-derivative estimation for clamped if not provided:
        // use finite difference at edges of the *node offsets* (per row/col).
        // For separable splines, we need end derivatives along X and along Y.
        double dxStartX = s.DxStart_Xoff ?? EstimateDxStart_Xoff();
        double dxEndX   = s.DxEnd_Xoff   ?? EstimateDxEnd_Xoff();
        double dxStartY = s.DxStart_Yoff ?? EstimateDxStart_Yoff();
        double dxEndY   = s.DxEnd_Yoff   ?? EstimateDxEnd_Yoff();

        double dyStartX = s.DyStart_Xoff ?? EstimateDyStart_Xoff();
        double dyEndX   = s.DyEnd_Xoff   ?? EstimateDyEnd_Xoff();
        double dyStartY = s.DyStart_Yoff ?? EstimateDyStart_Yoff();
        double dyEndY   = s.DyEnd_Yoff   ?? EstimateDyEnd_Yoff();

        // Step 1: spline along X for each row to get temp at xPosMm
        double[] tempXoff = new double[Rows];
        double[] tempYoff = new double[Rows];

        for (int iy = 0; iy < Rows; iy++)
        {
            Span<double> fx = stackalloc double[Columns];
            Span<double> fy = stackalloc double[Columns];
            for (int ix = 0; ix < Columns; ix++)
            {
                fx[ix] = NodeOffsets[iy, ix].Xoff;
                fy[ix] = NodeOffsets[iy, ix].Yoff;
            }

            tempXoff[iy] = CubicSpline1D_EvalUniformGrid(
                X0: X[0], h: Dx, values: fx, xQuery: xPosMm,
                boundary: s.Boundary,
                dStart: dxStartX,
                dEnd: dxEndX,
                allowExtrapolation: allowExtrapolation);

            tempYoff[iy] = CubicSpline1D_EvalUniformGrid(
                X0: X[0], h: Dx, values: fy, xQuery: xPosMm,
                boundary: s.Boundary,
                dStart: dxStartY,
                dEnd: dxEndY,
                allowExtrapolation: allowExtrapolation);
        }

        // Step 2: spline along Y over temp[]
        double xoffFinal = CubicSpline1D_EvalUniformGrid(
            X0: Y[0], h: Dy, values: tempXoff, xQuery: yPosMm,
            boundary: s.Boundary,
            dStart: dyStartX,
            dEnd: dyEndX,
            allowExtrapolation: allowExtrapolation);

        double yoffFinal = CubicSpline1D_EvalUniformGrid(
            X0: Y[0], h: Dy, values: tempYoff, xQuery: yPosMm,
            boundary: s.Boundary,
            dStart: dyStartY,
            dEnd: dyEndY,
            allowExtrapolation: allowExtrapolation);

        return new Offset2D(xoffFinal, yoffFinal);
    }

    // Global (simple) derivative estimates from corner differences.
    // If you need per-row/per-col derivatives, that’s also doable but heavier API.
    private double EstimateDxStart_Xoff() => (NodeOffsets[0, 1].Xoff - NodeOffsets[0, 0].Xoff) / Dx;
    private double EstimateDxEnd_Xoff()   => (NodeOffsets[0, Columns - 1].Xoff - NodeOffsets[0, Columns - 2].Xoff) / Dx;
    private double EstimateDxStart_Yoff() => (NodeOffsets[0, 1].Yoff - NodeOffsets[0, 0].Yoff) / Dx;
    private double EstimateDxEnd_Yoff()   => (NodeOffsets[0, Columns - 1].Yoff - NodeOffsets[0, Columns - 2].Yoff) / Dx;

    private double EstimateDyStart_Xoff() => (NodeOffsets[1, 0].Xoff - NodeOffsets[0, 0].Xoff) / Dy;
    private double EstimateDyEnd_Xoff()   => (NodeOffsets[Rows - 1, 0].Xoff - NodeOffsets[Rows - 2, 0].Xoff) / Dy;
    private double EstimateDyStart_Yoff() => (NodeOffsets[1, 0].Yoff - NodeOffsets[0, 0].Yoff) / Dy;
    private double EstimateDyEnd_Yoff()   => (NodeOffsets[Rows - 1, 0].Yoff - NodeOffsets[Rows - 2, 0].Yoff) / Dy;

    private static double CubicSpline1D_EvalUniformGrid(
        double X0,
        double h,
        ReadOnlySpan<double> values,
        double xQuery,
        SplineBoundary boundary,
        double dStart,
        double dEnd,
        bool allowExtrapolation)
    {
        int n = values.Length;
        if (n < 2) throw new ArgumentException("Need at least 2 points for spline.");

        // If extrapolation not allowed, clamp query to [X0, X0 + (n-1)h]
        double xMin = X0;
        double xMax = X0 + (n - 1) * h;
        if (!allowExtrapolation)
            xQuery = Math.Clamp(xQuery, xMin, xMax);

        // Segment selection:
        // For extrapolation we still use first segment (i=0) or last segment (i=n-2)
        double u = (xQuery - X0) / h;
        int i = (int)Math.Floor(u);
        if (i < 0) i = 0;
        if (i > n - 2) i = n - 2;

        double xi = X0 + i * h;
        double t = (xQuery - xi) / h; // may be <0 or >1 in extrapolation mode

        // Compute second derivatives M
        double[] M = BuildSecondDerivativesUniform(values, h, boundary, dStart, dEnd);

        double A = 1.0 - t;
        double B = t;

        double yi = values[i];
        double yi1 = values[i + 1];
        double Mi = M[i];
        double Mi1 = M[i + 1];

        return A * yi + B * yi1 + ((A * A * A - A) * Mi + (B * B * B - B) * Mi1) * (h * h) / 6.0;
    }

    private static double[] BuildSecondDerivativesUniform(ReadOnlySpan<double> values, double h, SplineBoundary boundary, double dStart, double dEnd)
    {
        int n = values.Length;
        double[] M = new double[n];

        if (n == 2)
        {
            M[0] = M[1] = 0;
            return M;
        }

        if (boundary == SplineBoundary.Natural)
        {
            int m = n - 2;
            double[] cPrime = new double[m];
            double[] dPrime = new double[m];

            for (int k = 0; k < m; k++)
            {
                int j = k + 1;
                double a = (k == 0) ? 0.0 : 1.0;
                double b = 4.0;
                double c = (k == m - 1) ? 0.0 : 1.0;
                double r = 6.0 * (values[j - 1] - 2.0 * values[j] + values[j + 1]) / (h * h);

                if (k == 0)
                {
                    cPrime[k] = c / b;
                    dPrime[k] = r / b;
                }
                else
                {
                    double denom = b - a * cPrime[k - 1];
                    cPrime[k] = c / denom;
                    dPrime[k] = (r - a * dPrime[k - 1]) / denom;
                }
            }

            for (int k = m - 1; k >= 0; k--)
            {
                int j = k + 1;
                double next = (k == m - 1) ? 0.0 : M[j + 1];
                M[j] = dPrime[k] - cPrime[k] * next;
            }

            M[0] = 0;
            M[n - 1] = 0;
            return M;
        }
        else // Clamped
        {
            double[] a = new double[n];
            double[] b = new double[n];
            double[] c = new double[n];
            double[] r = new double[n];

            a[0] = 0; b[0] = 2; c[0] = 1;
            r[0] = 6.0 / h * (((values[1] - values[0]) / h) - dStart);

            for (int j = 1; j <= n - 2; j++)
            {
                a[j] = 1; b[j] = 4; c[j] = 1;
                r[j] = 6.0 * (values[j - 1] - 2.0 * values[j] + values[j + 1]) / (h * h);
            }

            a[n - 1] = 1; b[n - 1] = 2; c[n - 1] = 0;
            r[n - 1] = 6.0 / h * (dEnd - ((values[n - 1] - values[n - 2]) / h));

            for (int j = 1; j < n; j++)
            {
                double w = a[j] / b[j - 1];
                b[j] -= w * c[j - 1];
                r[j] -= w * r[j - 1];
            }

            M[n - 1] = r[n - 1] / b[n - 1];
            for (int j = n - 2; j >= 0; j--)
                M[j] = (r[j] - c[j] * M[j + 1]) / b[j];

            return M;
        }
    }

    // ---------------------------
    // Misc helpers
    // ---------------------------

    private static int FindLowerIndex(double[] arr, double value)
    {
        int i = Array.BinarySearch(arr, value);
        if (i >= 0) return i;
        i = ~i;
        return i - 1;
    }
}
