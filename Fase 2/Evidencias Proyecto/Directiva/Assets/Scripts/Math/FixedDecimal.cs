using System;
using System.Globalization;
using JetBrains.Annotations;
using UnityEditorInternal;

namespace DMath
{
    /// <summary>
    /// Numero decimal de punto fijo determinista.
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>
    {
        public long RawValue { get; }

        public Fixed(long value, bool raw = false)
        {
            if (raw)
                this.RawValue = value;
            else
                this.RawValue = checked(value * Constants.FixedScale);
        }

        public static implicit operator Fixed(long value)
        {
            return new Fixed(value);
        }

        // Aritmética

        public static Fixed operator + (Fixed left, Fixed right)
        {
            return new Fixed(checked(left.RawValue + right.RawValue), raw: true);
        }

        public static Fixed operator - (Fixed left, Fixed right)
        {
            return new Fixed(checked(left.RawValue - right.RawValue), raw: true);
        }

        public static Fixed operator * (Fixed left, Fixed right)
        {
            // WARNING: Usar valores grandes puede generar overflow durante el proceso intermedio de la operación sin
            // Que necesariamente el resultado final haga overflow.
            // Esta implementación entiende que ningún valor razonable en una partida debería
            // causar overflow al operar.
            return new Fixed(checked((left.RawValue * right.RawValue)/Constants.FixedScale), raw: true);
        }

        public static Fixed operator / (Fixed left, Fixed right)
        {
            // WARNING: Para mejorar la precisión de la división decimal, esta función escala primero el numerador, lo
            // cual puede generar overflow.
            // Esta implementación entiende que ningún valor razonable en una partida debería
            // causar overflow al operar.
            return new Fixed(checked(left.RawValue * Constants.FixedScale / right.RawValue), raw: true);
        }

        public static Fixed operator % (Fixed left, Fixed right)
        {
            return new Fixed(left.RawValue % right.RawValue, raw: true);
        }

        public static Fixed operator -(Fixed value)
        {
            return new Fixed(checked(-value.RawValue), raw: true);
        }

        public static Fixed operator +(Fixed value)
        {
            return value;
        }

        public static Fixed operator ++(Fixed value)
        {
            return value + 1;
        }

        public static Fixed operator --(Fixed value)
        {
            return value - 1;
        }

        // Comparaciones

        public static bool operator ==(Fixed left, Fixed right)
        {
            return left.RawValue == right.RawValue;
        }

        public static bool operator !=(Fixed left, Fixed right)
        {
            return left.RawValue != right.RawValue;
        }

        public static bool operator >(Fixed left, Fixed right)
        {
            return left.RawValue > right.RawValue;
        }

        public static bool operator <(Fixed left, Fixed right)
        {
            return left.RawValue < right.RawValue;
        }

        public static bool operator >=(Fixed left, Fixed right)
        {
            return left.RawValue >= right.RawValue;
        }

        public static bool operator <=(Fixed left, Fixed right)
        {
            return left.RawValue <= right.RawValue;
        }

        // Logica

        public static bool operator true(Fixed value)
        {
            return value.RawValue != 0;
        }

        public static bool operator false(Fixed value)
        {
            return value.RawValue == 0;
        }

        public static bool operator !(Fixed value)
        {
            return value.RawValue == 0;
        }

        // Igualdad y Hashing

        public bool Equals(Fixed other)
        {
            return RawValue == other.RawValue;
        }

        public override bool Equals(object obj)
        {
            return obj is Fixed other && Equals(other);
        }

        public override int GetHashCode()
        {
            return RawValue.GetHashCode();
        }

        // Valor absoluto
        public Fixed Abs()
        {
            return RawValue >= 0 ? this : -this;
        }

        public static Fixed Abs(Fixed value)
        {
            return value.RawValue >= 0 ? value : -value;
        }

        // Min y Max
        public static Fixed Min(Fixed a, Fixed b)
        {
            return (a <= b) ? a : b;
        }

        public static Fixed Max(Fixed a, Fixed b)
        {
            return (a >= b) ? a : b;
        }


        // Clamp
        public Fixed Clamp(Fixed min, Fixed max)
        {
            if (min > max)
                return Clamp(max, min);
            if (this < min) return min;
            if (this > max) return max;
            return this;
        }

        public static Fixed Clamp(Fixed value, Fixed min, Fixed max)
        {
            if (min > max)
                return Clamp(value, max, min);
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // Raíz Cuadrada
        public static Fixed Sqrt(Fixed value)
        {
            if (value.RawValue < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            long scaled = checked(
                value.RawValue * Constants.FixedScale
            );

            long result = IntegerSqrt(scaled);

            return new Fixed(result, raw: true);
        }


        private static long IntegerSqrt(long value)
        {
            long low = 0;
            long high = Math.Min(value, 1L << 32);

            while (low < high)
            {
                long mid = low + (high - low + 1) / 2;

                if (mid <= value / mid)
                    low = mid;
                else
                    high = mid - 1;
            }

            return low;
        }

        // Conversion string
        public override string ToString()
        {
            return ToString(this);
        }

        public static string ToString(Fixed value)
        {
            long whole = value.RawValue / Constants.FixedScale;
            long decimalPart = value.RawValue % Constants.FixedScale;

            string wholeStr = whole.ToString(CultureInfo.InvariantCulture);

            // Preservar el signo de valores entre -1 y 0.
            if (value.RawValue < 0 && whole == 0)
                wholeStr = "-" + wholeStr;

            if (decimalPart == 0)
                return wholeStr + ".0";

            // Obtener magnitud de la parte decimal.
            if (decimalPart < 0)
                decimalPart = -decimalPart;

            string decimalStr = decimalPart
                .ToString("D4", CultureInfo.InvariantCulture)
                .TrimEnd('0');

            return wholeStr + "." + decimalStr;
        }

        // Redondeo (Los empates se alejan de cero)
        public Fixed Round()
        {
            return Round(this);
        }

        public static Fixed Round(Fixed value)
        {
            return RoundToLong(value);
        }

        public static long RoundToLong(Fixed value)
        {
            long decimalPart = value.RawValue % Constants.FixedScale;
            long integerPart = value.RawValue / Constants.FixedScale;

            if (decimalPart == 0) return integerPart;
            if (decimalPart >= Constants.FixedScale / 2) return ++integerPart;
            if (-decimalPart >= Constants.FixedScale / 2) return --integerPart;
            return integerPart;
        }


        /// <summary>
        /// Redondea hacia infinito positivo.
        /// Para valores negativos no enteros, esto equivale a acercarse a cero.
        /// </summary>
        public Fixed Ceil()
        {
            return Ceil(this);
        }

        /// <summary>
        /// Redondea hacia infinito positivo.
        /// Para valores negativos no enteros, esto equivale a acercarse a cero.
        /// </summary>
        public static Fixed Ceil(Fixed value)
        {
            return CeilToLong(value);
        }

        /// <summary>
        /// Redondea hacia infinito positivo.
        /// Para valores negativos no enteros, esto equivale a acercarse a cero.
        /// </summary>
        /// <returns>Parte entera como long</returns>
        public static long CeilToLong(Fixed value)
        {
            long decimalPart = value.RawValue % Constants.FixedScale;
            long integerPart = value.RawValue / Constants.FixedScale;

            return (decimalPart > 0) ? integerPart + 1 : integerPart;
        }

        /// <summary>
        /// Redondea hacia infinito negativo.
        /// </summary>
        public Fixed Floor()
        {
            return Floor(this);
        }

        /// <summary>
        /// Redondea hacia infinito negativo.
        /// </summary>
        public static Fixed Floor(Fixed value)
        {
            return FloorToLong(value);
        }

        /// <summary>
        /// Redondea hacia infinito negativo.
        /// </summary>
        /// <returns>Parte entera como long</returns>
        public static long FloorToLong(Fixed value)
        {
            long integerPart = value.RawValue / Constants.FixedScale;
            long decimalPart = value.RawValue % Constants.FixedScale;

            return decimalPart < 0 ? integerPart - 1 : integerPart;
        }
    }
}
