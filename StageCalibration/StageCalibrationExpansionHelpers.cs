public static class StageCalibrationExpansionHelpers
{
    public static StageCalibrationGrid ExpandSignedTop(
        this StageCalibrationGrid grid,
        int addTopRowsSigned,
        int addLeftCols = 0,
        int addRightCols = 0,
        OutOfGridPolicy fillPolicy = OutOfGridPolicy.ExtrapolatePlaneFit,
        StageCalibrationGrid.ExtrapolationSettings? ex = null,
        StageCalibrationGrid.SplineSettings? sp = null,
        InterpolationMethod interp = InterpolationMethod.Bilinear)
    {
        int addTop = addTopRowsSigned < 0 ? -addTopRowsSigned : 0;       // negative => below bottom (Y-)
        int addBottom = addTopRowsSigned > 0 ? addTopRowsSigned : 0;     // positive => above top (Y+)

        return grid.Expand(
            addLeftCols: addLeftCols,
            addRightCols: addRightCols,
            addTopRows: addTop,
            addBottomRows: addBottom,
            fillPolicy: fillPolicy,
            extrapSettings: ex,
            splineSettings: sp,
            interpForFill: interp);
    }
}
