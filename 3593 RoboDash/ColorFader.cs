using System;
using System.Collections.Generic;
using System.Drawing;

namespace _3593_RoboDash
{
    class ColorFader
    {
        private Color originalColor;
        private Color newColor;

        private double stepsR;
        private double stepsG;
        private double stepsB;

        private int stepCount;

        public ColorFader(Color from, Color to, int steps)
        {
            if (steps == 0)
                throw new ArgumentException("Steps must be at least 1");

            originalColor = from;
            newColor = to;
            stepCount = steps;

            stepsR = (double)(newColor.R - originalColor.R) / stepCount;
            stepsG = (double)(newColor.G - originalColor.G) / stepCount;
            stepsB = (double)(newColor.B - originalColor.B) / stepCount;
        }

        public IEnumerable<Color> Fade()
        {
            for (uint i = 0; i < stepCount; ++i)
            {
                yield return Color.FromArgb((int)(originalColor.R + i * stepsR), (int)(originalColor.G + i * stepsG), (int)(originalColor.B + i * stepsB));
            }
            yield return newColor; // make sure we always return the exact target color last
        }
    }
}
