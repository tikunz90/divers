using System;

namespace CalibRecalibrator
{
    /// <summary>
    /// Updates the calibration tables with interpolated residuals.
    /// </summary>
    public class CalibUpdater
    {
        private const double NM_PER_COUNT = 1220.0;  // nm per count
        private const double MM_PER_NM = 1e-6;       // mm per nm
        private const double MM_PER_COUNT = NM_PER_COUNT * MM_PER_NM;  // 0.00122 mm per count
        private const double COUNTS_PER_MM = 1.0 / MM_PER_COUNT;  // 819.672131147541 counts per mm

        private readonly BaseCalibParser baseCalib;
        private readonly BilinearInterpolator interpolator;

        public CalibUpdater(BaseCalibParser baseCalib, BilinearInterpolator interpolator)
        {
            this.baseCalib = baseCalib;
            this.interpolator = interpolator;
        }

        public void ApplyResiduals(MeasuredDataParser measuredData)
        {
            int[,] newXTable = new int[33, 33];
            int[,] newYTable = new int[33, 33];

            // Fixed grid: 33x33 nodes, pitch = 10 mm, centered origin
            // u,v = 0..32 map to x = (u-16)*10, y = (v-16)*10 mm
            for (int v = 0; v < 33; v++)
            {
                for (int u = 0; u < 33; u++)
                {
                    double x = (u - 16) * 10.0;  // mm
                    double y = (v - 16) * 10.0;  // mm

                    // Interpolate residuals at this point
                    double xResidualMm = interpolator.Interpolate(x, y, measuredData.XResiduals);
                    double yResidualMm = interpolator.Interpolate(x, y, measuredData.YResiduals);

                    // Convert mm to counts
                    int xDeltaCounts = ConvertMmToCounts(xResidualMm);
                    int yDeltaCounts = ConvertMmToCounts(yResidualMm);

                    // Apply update: New = Base + Delta
                    newXTable[v, u] = baseCalib.XTable[v, u] + xDeltaCounts;
                    newYTable[v, u] = baseCalib.YTable[v, u] + yDeltaCounts;
                }
            }

            baseCalib.UpdateTables(newXTable, newYTable);
        }

        private int ConvertMmToCounts(double mm)
        {
            // deltaCounts = round(deltaMm * 819.672131147541)
            // Using MidpointRounding.AwayFromZero for halfway cases
            return (int)Math.Round(mm * COUNTS_PER_MM, MidpointRounding.AwayFromZero);
        }
    }
}
