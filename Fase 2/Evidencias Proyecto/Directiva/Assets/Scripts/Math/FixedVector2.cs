using System;

namespace DMath
{
    /// <summary>
    /// Vector bidimensional determinista  .
    /// </summary>
    public readonly struct FixedVector2 : IEquatable<FixedVector2>
    {
        public Fixed X { get; }
        public Fixed Y { get; }

        public FixedVector2(Fixed x, Fixed y)
        {
            X = x;
            Y = y;
        }

        public static FixedVector2 Zero => new FixedVector2(0, 0);
        public static FixedVector2 One => new FixedVector2(1, 1);
        public static FixedVector2 Right => new FixedVector2(1, 0);
        public static FixedVector2 Left => new FixedVector2(-1, 0);
        public static FixedVector2 Up => new FixedVector2(0, 1);
        public static FixedVector2 Down => new FixedVector2(0, -1);

        // Operaciones vectoriales

        public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b)
        {
            return new FixedVector2(a.X + b.X, a.Y + b.Y);
        }

        public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b)
        {
            return new FixedVector2(a.X - b.X, a.Y - b.Y);
        }

        public static FixedVector2 operator -(FixedVector2 value)
        {
            return new FixedVector2(-value.X, -value.Y);
        }

        public static FixedVector2 operator +(FixedVector2 value)
        {
            return value;
        }

        public static FixedVector2 operator *(FixedVector2 vector, Fixed scalar)
        {
            return new FixedVector2(vector.X * scalar, vector.Y * scalar);
        }

        public static FixedVector2 operator *(Fixed scalar, FixedVector2 vector)
        {
            return vector * scalar;
        }

        public static FixedVector2 operator /(FixedVector2 vector, Fixed scalar)
        {
            return new FixedVector2(vector.X / scalar, vector.Y / scalar);
        }

        // Rotaciones exactas en incrementos de 90 grados.
        // Un giro positivo es antihorario: (1, 0) pasa a (0, 1).

        public FixedVector2 Rotate90()
        {
            return new FixedVector2(-Y, X);
        }

        public FixedVector2 Rotate180()
        {
            return new FixedVector2(-X, -Y);
        }

        public FixedVector2 Rotate270()
        {
            return new FixedVector2(Y, -X);
        }

        // Producto punto

        public Fixed Dot(FixedVector2 other)
        {
            return Dot(this, other);
        }

        public static Fixed Dot(FixedVector2 a, FixedVector2 b)
        {
            // Sumamos ambos productos antes de dividir por la escala,
            // para no truncar cada componente por separado.
            long xProduct = checked(a.X.RawValue * b.X.RawValue);
            long yProduct = checked(a.Y.RawValue * b.Y.RawValue);
            long sum = checked(xProduct + yProduct);

            return new Fixed(sum / Constants.FixedScale, raw: true);
        }

        // Magnitudes y distancias

        public Fixed SqrMagnitude => Dot(this, this);

        public Fixed Magnitude
        {
            get
            {
                // sqrt(Xraw^2 + Yraw^2) ya esta en unidades raw.
                // Evita perder la magnitud de vectores pequenos al
                // calcular primero SqrMagnitude con precision Fixed.
                long rawSquared = RawSquaredMagnitude();
                return new Fixed(IntegerSqrt(rawSquared), raw: true);
            }
        }

        public static Fixed SqrDistance(FixedVector2 a, FixedVector2 b)
        {
            return (a - b).SqrMagnitude;
        }

        public static Fixed Distance(FixedVector2 a, FixedVector2 b)
        {
            return (a - b).Magnitude;
        }

        // Normalizacion y limite de longitud

        public FixedVector2 Normalized
        {
            get
            {
                // Escalamos primero por la mayor componente. Esto evita
                // que una longitud muy pequena se trunque antes de dividir.
                Fixed largest = Fixed.Max(Fixed.Abs(X), Fixed.Abs(Y));
                if (largest == 0)
                    return Zero;

                FixedVector2 scaled = this / largest;
                long scaledLength = IntegerSqrtCeiling(scaled.RawSquaredMagnitude());
                return scaled / new Fixed(scaledLength, raw: true);
            }
        }

        public FixedVector2 ClampMagnitude(Fixed maxMagnitude)
        {
            if (maxMagnitude < 0)
                throw new ArgumentOutOfRangeException(nameof(maxMagnitude));

            // Comparamos cuadrados raw para evitar perder precision
            // cuando el cuadrado no cabe en la resolucion de Fixed.
            long squared = RawSquaredMagnitude();
            long maxSquared = checked(maxMagnitude.RawValue * maxMagnitude.RawValue);

            if (squared <= maxSquared)
                return this;

            // Equivale a normalizar y multiplicar por maxMagnitude,
            // pero con una sola division y sin redondeos intermedios.
            // Redondear la longitud hacia arriba garantiza que el
            // resultado no supere el limite por error de truncamiento.
            long lengthCeil = IntegerSqrtCeiling(squared);

            long x = checked(X.RawValue * maxMagnitude.RawValue) / lengthCeil;
            long y = checked(Y.RawValue * maxMagnitude.RawValue) / lengthCeil;

            return new FixedVector2(
                new Fixed(x, raw: true),
                new Fixed(y, raw: true)
            );
        }

        public static FixedVector2 ClampMagnitude(FixedVector2 vector, Fixed maxMagnitude)
        {
            return vector.ClampMagnitude(maxMagnitude);
        }

        // Igualdad y hashing

        public static bool operator ==(FixedVector2 a, FixedVector2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(FixedVector2 a, FixedVector2 b)
        {
            return !(a == b);
        }

        public bool Equals(FixedVector2 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is FixedVector2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }

        private long RawSquaredMagnitude()
        {
            long xSquared = checked(X.RawValue * X.RawValue);
            long ySquared = checked(Y.RawValue * Y.RawValue);
            return checked(xSquared + ySquared);
        }

        private static long IntegerSqrtCeiling(long value)
        {
            long root = IntegerSqrt(value);
            return checked(root * root) == value ? root : root + 1;
        }

        // Raiz cuadrada entera: floor(sqrt(value)), sin floats.
        // La implementacion de IntegerSqrt en Fixed es privada,
        // por lo que este helper vive aqui sin modificar Fixed.cs.
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

        public static FixedVector2 FromAngle(Fixed angle)
        {
            return new FixedVector2(
                Trigonometry.Cos(angle),
                Trigonometry.Sin(angle)
                );
        }
    }
}
