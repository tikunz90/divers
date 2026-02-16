using System;
using System.Collections.Generic;
using System.Linq;

namespace StageCalibration {
    public struct Offset2D {
        public double XOffset;
        public double YOffset;

        public Offset2D(double xOffset, double yOffset) {
            XOffset = xOffset;
            YOffset = yOffset;
        }
    }

    public enum OutOfGridPolicy {
        Clamp,
        ReturnNaN,
        Throw
    }

    public class StageCalibrationGrid {
        private Dictionary<(int, int), Offset2D> _offsets;
        private int _rows;
        private int _cols;
        private double _xStep;
        private double _yStep;
        private OutOfGridPolicy _outOfGridPolicy;

        public StageCalibrationGrid(double xStep, double yStep, OutOfGridPolicy policy) {
            _offsets = new Dictionary<(int, int), Offset2D>();
            _xStep = xStep;
            _yStep = yStep;
            _outOfGridPolicy = policy;
        }

        public void ParseCalibrationTable(string[] lines) {
            // Assume the first line contains headers
            var dimensions = lines[0].Split(';').Select(d => d.Split(':')[1]).ToArray();
            _cols = int.Parse(dimensions[0]);
            _rows = int.Parse(dimensions[1]);
            
            for (int i = 1; i < lines.Length; i++) {
                var values = lines[i].Split(';');
                int nomX = int.Parse(values[0]);
                double actX = double.Parse(values[1]);
                int nomY = int.Parse(values[2]);
                double actY = double.Parse(values[3]);

                _offsets[(nomX, nomY)] = new Offset2D(actX - nomX, actY - nomY);
            }
        }

        public double GetOffset(int x, int y, string method) {
            (double xOffset, double yOffset) = GetWeights(x, y);
            switch (method) {
                case "bilinear":
                    return BilinearInterpolation(x, y, xOffset, yOffset);
                case "bicubic":
                    return BicubicInterpolation(x, y);
                case "cubicSpline":
                    return CubicSplineInterpolation(x, y);
                default:
                    throw new ArgumentException("Unknown interpolation method.");
            }
        }

        private (double, double) GetWeights(int x, int y) {
            // Implement logic to determine weights for interpolation
            return (0.0, 0.0);
        }

        private double BilinearInterpolation(int x, int y, double xWeight, double yWeight) {
            // Apply bilinear interpolation logic
            return 0.0; // Replace with actual calculation
        }

        private double BicubicInterpolation(int x, int y) {
            // Apply bicubic interpolation logic
            return 0.0; // Replace with actual calculation
        }

        private double CubicSplineInterpolation(int x, int y) {
            // Apply cubic spline interpolation logic
            return 0.0; // Replace with actual calculation
        }
    }
}