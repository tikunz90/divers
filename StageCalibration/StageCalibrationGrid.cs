using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace StageCalibration
{
    public enum InterpolationMethod
    {
        Bilinear,
        BicubicCatmullRom,
        CubicSplineSeparable
    }

    public enum OutOfGridPolicy
    {
        Clamp,
        ReturnNaN,
        Throw
    }

    public enum SplineBoundary
    {
        Natural, // second derivative = 0 at ends
        Clamped  // first derivative specified at ends
    }

    public readonly record struct Offset2D(double Xoff, double Yoff);

    /// <summary>
    /// Parses and interpolates planar 2D stage calibration tables:
    /// Header:
    ///   X:{totalWidthMm};{columns}
    ///   Y:{totalHeightMm};{rows}
    /// Then exactly rows*cols lines:
    ///   {nomX};{actX};{nomY};{actY};
    /// Offsets are stored as (act - nom).
    /// Grid spacing must be uniform (but may differ between stages).
    /// </summary>
    public sealed class StageCalibrationGrid
    {
        public int Columns { get; }
        public int Rows { get; }
        public double TotalWidthMm { get; }
        public double TotalHeightMm { get; }

        public double[] X { get; } // size Columns
        public double[] Y { get; } // size Rows

        public double Dx { get; }
        public double Dy { get; }

        /// <summary>[row, col] => [yIndex, xIndex]</summary>
        public Offset2D[,] NodeOffsets { get; }

        private const double UniformTol = 1e-9;

        internal StageCalibrationGrid(
            int columns, int rows,
            double totalWidthMm, double totalHeightMm,
            double[] x, double[] y,
            Offset2D[,] nodeOffsets)
        {
            Columns = columns;
            Rows = rows;
            TotalWidthMm = totalWidthMm;
            TotalHeightMm = totalHeightMm;
            X = x;
            Y = y;
            NodeOffsets = nodeOffsets;

            Dx = InferUniformStep(X, "X");
            Dy = InferUniformStep(Y, "Y");
        }

        public static StageCalibrationGrid ParseFile(string path)
            => ParseLines(File.ReadLines(path));

        public static StageCalibrationGrid ParseText(string text)
            => ParseLines(ReadLines(text));

        private static IEnumerable<string> ReadLines(string text)
        {
            using var sr = new StringReader(text);
            string? line;
            while ((line = sr.ReadLine()) != null)
                yield return line;
        }

        public static StageCalibrationGrid ParseLines(IEnumerable<string> lines)
        {
            using var e = lines.GetEnumerator();

            string lineX = NextNonEmpty(e) ?? throw new FormatException("Missing X header line.");
            string lineY = NextNonEmpty(e) ?? throw new FormatException("Missing Y header line.");

            ParseHeader(lineX, 'X', out double totalW, out int cols);
            ParseHeader(lineY, 'Y', out double totalH, out int rows);

            int expected = rows * cols;
            var records = new List<(double nomX, double actX, double nomY, double actY)>(expected);

            while (e.MoveNext())
            {
                var line = e.Current?.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4)
                    throw new FormatException($"Invalid data line (expected 4 fields): '{line}'");

                double nomX = ParseDouble(parts[0]);
                double actX = ParseDouble(parts[1]);
                double nomY = ParseDouble(parts[2]);
                double actY = ParseDouble(parts[3]);

                records.Add((nomX, actX, nomY, actY));
                if (records.Count == expected) break;
            }

            if (records.Count != expected)
                throw new FormatException($"Expected {expected} data lines but got {records.Count}.");

            var xs = records.Select(r => r.nomX).Distinct().OrderBy(v => v).ToArray();
            var ys = records.Select(r => r.nomY).Distinct().OrderBy(v => v).ToArray();

            if (xs.Length != cols)
                throw new FormatException($"Header columns={cols}, but found {*
