﻿using Intervallo.InternalUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Intervallo.DefaultPlugins.WORLD
{
    enum WindowType
    {
        Hanning,
        Blackman
    }

    public class D4C
    {
        const double DefaultThreshold = 0.85;
        const double FloorF0D4C = 47.0;
        const double SafeGuardMinimum = 0.000000000001;
        const double UpperLimit = 15000.0;
        const double FrequencyInterval = 3000.0;

        public double Threshold { get; set; }

        private XorShift Rand { get; }

        public D4C()
        {
            Threshold = DefaultThreshold;
            Rand = new XorShift();
        }

        public void Estimate(double[] x, int fs, double[] temporalPositions, double[] f0, int f0Length, int fftSize, double[][] aperiodicity)
        {
            aperiodicity.ForEach((a) => a.Fill(1.0 - SafeGuardMinimum));

            var fftSizeD4C = (int)Math.Pow(2.0, 1.0 + (int)MathUtil.Log2(4.0 * fs / FloorF0D4C + 1.0));

            var forwardRealFFT = ForwardRealFFT.Create(fftSizeD4C);

            var numberOfAperiodicities = (int)(Math.Min(UpperLimit, fs / 2.0 - FrequencyInterval) / FrequencyInterval);

            // Since the window function is common in D4CGeneralBody(),
            // it is designed here to speed up.
            var windowLength = (int)(FrequencyInterval * fftSizeD4C / fs) * 2 + 1;
            var window = new double[windowLength];
            Common.NuttallWindow(windowLength, window);

            // D4C Love Train (Aperiodicity of 0 Hz is given by the different algorithm)
            var aperiodicity0 = new double[f0Length];
            D4CLoveTrain(x, fs, f0, f0Length, temporalPositions, aperiodicity0);

            var coarseAperiodicity = new double[numberOfAperiodicities + 2];
            coarseAperiodicity[0] = -60.0;
            coarseAperiodicity[numberOfAperiodicities + 1] = -SafeGuardMinimum;
            var coarseFrequencyAxis = Enumerable.Range(0, numberOfAperiodicities + 2).Select((i) => i * FrequencyInterval).ToArray();
            coarseFrequencyAxis[numberOfAperiodicities + 1] = fs / 2.0;

            var frequencyAxis = Enumerable.Range(0, fftSize / 2 + 1).Select((i) => (double)i * fs / fftSize).ToArray();

            for (var i = 0; i < f0Length; i++)
            {
                if (f0[i] == 0.0 || aperiodicity0[i] <= Threshold)
                {
                    continue;
                }

                D4CGeneralBody(x, fs, Math.Max(FloorF0D4C, f0[i]), fftSizeD4C, temporalPositions[i], numberOfAperiodicities, window, forwardRealFFT, coarseAperiodicity.SubSequence(1));

                // Linear interpolation to convert the coarse aperiodicity into its
                // spectral representation.
                GetAperiodicity(coarseFrequencyAxis, coarseAperiodicity, numberOfAperiodicities, frequencyAxis, fftSize, aperiodicity[i]);
            }
        }

        void GetAperiodicity(double[] coarseFrequencyAxis, double[] coarseAperiodicity, int numberOfAperiodicities, double[] frequencyAxis, int fftSize, double[] aperiodicity)
        {
            MatlabFunctions.Interp1(coarseFrequencyAxis.SubSequence(0, numberOfAperiodicities + 2), coarseAperiodicity, frequencyAxis.SubSequence(0, fftSize / 2 + 1), aperiodicity);
            for (int i = 0, limit = fftSize / 2; i <= limit; i++)
            {
                aperiodicity[i] = Math.Pow(10.0, aperiodicity[i] / 20.0);
            }
        }

        //-----------------------------------------------------------------------------
        // D4CGeneralBody() calculates a spectral envelope at a temporal
        // position. This function is only used in D4C().
        // Caution:
        //   forward_fft is allocated in advance to speed up the processing.
        //-----------------------------------------------------------------------------
        void D4CGeneralBody(double[] x, int fs, double currentF0, int fftSize, double currentPosition, int numberOfAperiodicities, double[] window, ForwardRealFFT forwardRealFFT, SubSequence<double> coarseAperiodicity)
        {
            var staticCentroid = new double[fftSize / 2 + 1];
            var smoothedPowerSpectrum = new double[fftSize / 2 + 1];
            var staticGroupDelay = new double[fftSize / 2 + 1];
            GetStaticCentroid(x, fs, currentF0, fftSize, currentPosition, forwardRealFFT, staticCentroid);
            GetSmoothedPowerSpectrum(x, fs, currentF0, fftSize, currentPosition, forwardRealFFT, smoothedPowerSpectrum);
            GetStaticGroupDelay(staticCentroid, smoothedPowerSpectrum, fs, currentF0, fftSize, staticGroupDelay);

            GetCoarseAperiodicity(staticGroupDelay, fs, fftSize, numberOfAperiodicities, window, forwardRealFFT, coarseAperiodicity);

            // Revision of the result based on the F0
            for (var i = 0; i < numberOfAperiodicities; i++)
            {
                coarseAperiodicity[i] = Math.Min(0.0, coarseAperiodicity[i] + (currentF0 - 100) / 50.0);
            }
        }

        //-----------------------------------------------------------------------------
        // GetCoarseAperiodicity() calculates the aperiodicity in multiples of 3 kHz.
        // The upper limit is given based on the sampling frequency.
        //-----------------------------------------------------------------------------
        private void GetCoarseAperiodicity(double[] staticGroupDelay, int fs, int fftSize, int numberOfAperiodicities, double[] window, ForwardRealFFT forwardRealFFT, SubSequence<double> coarseAperiodicity)
        {
            var boundary = MatlabFunctions.MatlabRound(fftSize * 8.0 / window.Length);
            var halfWindowLength = window.Length / 2;

            Array.Clear(forwardRealFFT.Waveform, 0, fftSize);

            var powerSpectrum = new double[fftSize / 2 + 1];
            for (var i = 0; i < numberOfAperiodicities; i++)
            {
                var center = (int)(FrequencyInterval * (i + 1) * fftSize / fs);
                for (int j = 0, limit = halfWindowLength * 2; j <= limit; j++)
                {
                    forwardRealFFT.Waveform[j] = staticGroupDelay[center - halfWindowLength + j] * window[j];
                }
                FFT.Execute(forwardRealFFT.ForwardFFT);

                var spectrum = forwardRealFFT.Spectrum;
                for (int j = 0, limit = fftSize / 2; j <= limit; j++)
                {
                    powerSpectrum[j] = spectrum[j].Real * spectrum[j].Real + spectrum[j].Imaginary * spectrum[j].Imaginary;
                }

                Array.Sort(powerSpectrum);
                for (int j = 1, limit = fftSize / 2; j <= limit; j++)
                {
                    powerSpectrum[j] += powerSpectrum[j - 1];
                }

                coarseAperiodicity[i] = 10.0 * Math.Log10(powerSpectrum[fftSize / 2 - boundary - 1] / powerSpectrum[fftSize / 2]);
            }
        }

        //-----------------------------------------------------------------------------
        // GetStaticGroupDelay() calculates the temporally static group delay.
        // This is the fundamental parameter in D4C.
        //-----------------------------------------------------------------------------
        void GetStaticGroupDelay(double[] staticCentroid, double[] smoothedPowerSpectrum, int fs, double f0, int fftSize, double[] staticGroupDelay)
        {
            for (int i = 0, limit = fftSize / 2; i <= limit; i++)
            {
                staticGroupDelay[i] = staticCentroid[i] / smoothedPowerSpectrum[i];
            }
            Common.LinearSmoothing(staticGroupDelay, f0 / 2.0, fs, fftSize, staticGroupDelay);

            var smoothedGroupDelay = new double[fftSize / 2 + 1];
            Common.LinearSmoothing(staticGroupDelay, f0, fs, fftSize, smoothedGroupDelay);

            for (int i = 0, limit = fftSize / 2; i <= limit; i++)
            {
                staticGroupDelay[i] -= smoothedGroupDelay[i];
            }
        }

        //-----------------------------------------------------------------------------
        // GetSmoothedPowerSpectrum() calculates the smoothed power spectrum.
        // The parameters used for smoothing are optimized in davance.
        //-----------------------------------------------------------------------------
        void GetSmoothedPowerSpectrum(double[] x, int fs, double currentF0, int fftSize, double currentPosition, ForwardRealFFT forwardRealFFT, double[] smoothedPowerSpectrum)
        {
            Array.Clear(forwardRealFFT.Waveform, 0, fftSize);
            GetWindowedWaveform(x, fs, currentF0, currentPosition, WindowType.Hanning, 4.0, forwardRealFFT.Waveform);

            FFT.Execute(forwardRealFFT.ForwardFFT);
            var spectrum = forwardRealFFT.Spectrum;
            for (int i = 0, limit = fftSize / 2; i <= limit; i++)
            {
                smoothedPowerSpectrum[i] = spectrum[i].Real * spectrum[i].Real + spectrum[i].Imaginary * spectrum[i].Imaginary;
            }
            Common.DCCorrection(smoothedPowerSpectrum, currentF0, fs, fftSize, smoothedPowerSpectrum);
            Common.LinearSmoothing(smoothedPowerSpectrum, currentF0, fs, fftSize, smoothedPowerSpectrum);
        }

        //-----------------------------------------------------------------------------
        // GetStaticCentroid() calculates the temporally static energy centroid.
        // Basic idea was proposed by H. Kawahara.
        //-----------------------------------------------------------------------------
        void GetStaticCentroid(double[] x, int fs, double currentF0, int fftSize, double currentPosition, ForwardRealFFT forwardRealFFT, double[] staticCentroid)
        {
            var centroid1 = new double[fftSize / 2 + 1];
            var centroid2 = new double[fftSize / 2 + 1];

            GetCentroid(x, fs, currentF0, fftSize, currentPosition - 0.25 / currentF0, forwardRealFFT, centroid1);
            GetCentroid(x, fs, currentF0, fftSize, currentPosition + 0.25 / currentF0, forwardRealFFT, centroid2);

            for (int i = 0, limit = fftSize / 2; i <= limit; i++)
            {
                staticCentroid[i] = centroid1[i] + centroid2[i];
            }

            Common.DCCorrection(staticCentroid, currentF0, fs, fftSize, staticCentroid);

        }

        void GetCentroid(double[] x, int fs, double currentF0, int fftSize, double currentPosition, ForwardRealFFT forwardRealFFT, double[] centroid)
        {
            Array.Clear(forwardRealFFT.Waveform, 0, fftSize);
            GetWindowedWaveform(x, fs, currentF0, currentPosition, WindowType.Blackman, 4.0, forwardRealFFT.Waveform);

            var windowRange = MatlabFunctions.MatlabRound(2.0 * fs / currentF0) * 2;
            var power = Math.Sqrt(forwardRealFFT.Waveform.Take(windowRange).Sum((w) => w * w));
            for (var i = 0; i <= windowRange; i++)
            {
                forwardRealFFT.Waveform[i] /= power;
            }

            FFT.Execute(forwardRealFFT.ForwardFFT);
            var tmpSpectrum = new Complex[fftSize / 2 + 1];
            Array.Copy(forwardRealFFT.Spectrum, tmpSpectrum, tmpSpectrum.Length);

            for (var i = 0; i < fftSize; i++)
            {
                forwardRealFFT.Waveform[i] *= i + 1.0;
            }
            FFT.Execute(forwardRealFFT.ForwardFFT);
            var spectrum = forwardRealFFT.Spectrum;
            for (int i = 0, limit = fftSize / 2; i <= limit; i++)
            {
                centroid[i] = spectrum[i].Real * tmpSpectrum[i].Real + spectrum[i].Imaginary * tmpSpectrum[i].Imaginary;
            }
        }

        //-----------------------------------------------------------------------------
        // GetWindowedWaveform() windows the waveform by F0-adaptive window
        // In the variable window_type, 1: hanning, 2: blackman
        //-----------------------------------------------------------------------------
        void GetWindowedWaveform(double[] x, int fs, double currentF0, double currentPosition, WindowType windowType, double windowLengthRatio, double[] waveform)
        {
            var halfWindowLength = MatlabFunctions.MatlabRound(windowLengthRatio * fs / currentF0 / 2.0);

            var baseIndex = new int[halfWindowLength * 2 + 1];
            var safeIndex = new int[halfWindowLength * 2 + 1];
            var window = new double[halfWindowLength * 2 + 1];

            SetParametersForGetWindowedWaveform(halfWindowLength, x.Length, currentPosition, fs, currentF0, windowType, windowLengthRatio, baseIndex, safeIndex, window);

            for (int i = 0, limit = halfWindowLength * 2; i <= limit; i++)
            {
                waveform[i] = x[safeIndex[i]] * window[i] + Rand.Next() * SafeGuardMinimum;
            }

            var tmpWeight1 = waveform.Sum();
            var tmpWeight2 = window.Sum();
            var weightingCoefficient = tmpWeight1 / tmpWeight2;
            for (int i = 0, limit = halfWindowLength * 2; i <= limit; i++)
            {
                waveform[i] -= window[i] * weightingCoefficient;
            }
        }

        //-----------------------------------------------------------------------------
        // SetParametersForGetWindowedWaveform()
        //-----------------------------------------------------------------------------
        void SetParametersForGetWindowedWaveform(int halfWindowLength, int xLength, double currentPosition, int fs, double currentF0, WindowType windowType, double windowLengthRatio, int[] baseIndex, int[] safeIndex, double[] window)
        {
            for (var i = -halfWindowLength; i <= halfWindowLength; i++)
            {
                baseIndex[i + halfWindowLength] = i;
            }
            var origin = MatlabFunctions.MatlabRound(currentPosition * fs + 0.001);
            for (int i = 0, limit = halfWindowLength * 2; i <= limit; i++)
            {
                safeIndex[i] = Math.Min(xLength - 1, Math.Max(0, origin + baseIndex[i]));
            }

            switch (windowType)
            {
                case WindowType.Hanning:
                    for (int i = 0, limit = halfWindowLength * 2; i <= limit; i++)
                    {
                        var position = (2.0 * baseIndex[i] / windowLengthRatio) / fs;
                        window[i] = 0.5 * Math.Cos(Math.PI * position * currentF0) + 0.5;
                    }
                    break;
                case WindowType.Blackman:
                    for (int i = 0, limit = halfWindowLength * 2; i <= limit; i++)
                    {
                        var position = (2.0 * baseIndex[i] / windowLengthRatio) / fs;
                        window[i] = 0.42 + 0.5 * Math.Cos(Math.PI * position * currentF0) + 0.08 * Math.Cos(Math.PI * position * currentF0 * 2);
                    }
                    break;
            }
        }

        //-----------------------------------------------------------------------------
        // D4CLoveTrain() determines the aperiodicity with VUV detection.
        // If a frame was determined as the unvoiced section, aperiodicity is set to
        // very high value as the safeguard.
        // If it was voiced section, the aperiodicity of 0 Hz is set to -60 dB.
        //-----------------------------------------------------------------------------
        void D4CLoveTrain(double[] x, int fs, double[] f0, int f0Length, double[] temporalPositions, double[] aperiodicity0)
        {
            var lowestF0 = 40.0;
            var fftSize = (int)Math.Pow(2.0, 1.0 + (int)MathUtil.Log2(3.0 * fs / lowestF0 + 1));
            var forwardRealFFT = ForwardRealFFT.Create(fftSize);

            // Cumulative powers at 100, 4000, 7900 Hz are used for VUV identification.
            var boundary0 = (int)Math.Ceiling(100.0 * fftSize / fs);
            var boundary1 = (int)Math.Ceiling(4000.0 * fftSize / fs);
            var boundary2 = (int)Math.Ceiling(7900.0 * fftSize / fs);
            for (var i = 0; i < f0Length; i++)
            {
                if (f0[i] == 0.0)
                {
                    aperiodicity0[i] = 0.0;
                    continue;
                }

                aperiodicity0[i] = D4CLoveTrainSub(x, fs, Math.Max(f0[i], lowestF0), temporalPositions[i], f0Length, fftSize, boundary0, boundary1, boundary2, forwardRealFFT);
            }
        }

        double D4CLoveTrainSub(double[] x, int fs, double currentF0, double currentPosition, int f0Length, int fftSize, int boundary0, int boundary1, int boundary2, ForwardRealFFT forwardRealFFT)
        {
            var powerSpectrum = new double[fftSize];

            var windowLength = MatlabFunctions.MatlabRound(1.5 * fs / currentF0) * 2 + 1;
            GetWindowedWaveform(x, fs, currentF0, currentPosition, WindowType.Blackman, 3.0, forwardRealFFT.Waveform);

            Array.Clear(forwardRealFFT.Waveform, windowLength, fftSize - windowLength);
            FFT.Execute(forwardRealFFT.ForwardFFT);

            var spectrum = forwardRealFFT.Spectrum;
            for (int i = boundary0 + 1, limit = fftSize / 2 + 1; i < limit; i++)
            {
                powerSpectrum[i] = spectrum[i].Real * spectrum[i].Real + spectrum[i].Imaginary * spectrum[i].Imaginary;
            }
            for (var i = boundary0; i <= boundary2; i++)
            {
                powerSpectrum[i] += powerSpectrum[i - 1];
            }

            return powerSpectrum[boundary1] / powerSpectrum[boundary2];
        }
    }
}
