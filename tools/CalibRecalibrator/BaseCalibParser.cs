using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CalibRecalibrator
{
    /// <summary>
    /// Parses the base calibration file containing X and Y 33x33 tables.
    /// </summary>
    public class BaseCalibParser
    {
        public int[,] XTable { get; private set; }
        public int[,] YTable { get; private set; }
        public List<string> FileLines { get; private set; }
        public int XStartLine { get; private set; }
        public int YStartLine { get; private set; }

        public BaseCalibParser()
        {
            XTable = new int[33, 33];
            YTable = new int[33, 33];
            FileLines = new List<string>();
        }

        public void Parse(string filePath)
        {
            FileLines = File.ReadAllLines(filePath).ToList();
            
            // Find X at Z=0 section
            int xIndex = -1;
            int yIndex = -1;
            
            for (int i = 0; i < FileLines.Count; i++)
            {
                if (FileLines[i].Trim() == "X at Z=0")
                {
                    xIndex = i;
                }
                else if (FileLines[i].Trim() == "Y at Z=0")
                {
                    yIndex = i;
                }
            }

            if (xIndex == -1)
                throw new InvalidDataException("Could not find 'X at Z=0' section in base calibration file");
            if (yIndex == -1)
                throw new InvalidDataException("Could not find 'Y at Z=0' section in base calibration file");

            // Parse X table (skip "X at Z=0" and "Scale: x 1220 nm" lines)
            XStartLine = xIndex + 2;
            ParseTable(XStartLine, XTable);

            // Parse Y table (skip "Y at Z=0" and "Scale: x 1220 nm" lines)
            YStartLine = yIndex + 2;
            ParseTable(YStartLine, YTable);
        }

        private void ParseTable(int startLine, int[,] table)
        {
            for (int row = 0; row < 33; row++)
            {
                if (startLine + row >= FileLines.Count)
                    throw new InvalidDataException($"Insufficient data in base calibration file at line {startLine + row}");

                string line = FileLines[startLine + row];
                string[] parts = line.Split('\t');

                if (parts.Length != 33)
                    throw new InvalidDataException($"Expected 33 values in row {row + 1}, got {parts.Length} at line {startLine + row + 1}");

                for (int col = 0; col < 33; col++)
                {
                    if (!int.TryParse(parts[col], out int value))
                        throw new InvalidDataException($"Invalid integer value '{parts[col]}' at row {row + 1}, col {col + 1}");
                    
                    table[row, col] = value;
                }
            }
        }

        public void UpdateTables(int[,] newXTable, int[,] newYTable)
        {
            // Update X table lines
            for (int row = 0; row < 33; row++)
            {
                var values = new List<string>();
                for (int col = 0; col < 33; col++)
                {
                    values.Add(newXTable[row, col].ToString());
                }
                FileLines[XStartLine + row] = string.Join("\t", values);
            }

            // Update Y table lines
            for (int row = 0; row < 33; row++)
            {
                var values = new List<string>();
                for (int col = 0; col < 33; col++)
                {
                    values.Add(newYTable[row, col].ToString());
                }
                FileLines[YStartLine + row] = string.Join("\t", values);
            }
        }

        public void WriteToFile(string filePath)
        {
            File.WriteAllLines(filePath, FileLines);
        }
    }
}
