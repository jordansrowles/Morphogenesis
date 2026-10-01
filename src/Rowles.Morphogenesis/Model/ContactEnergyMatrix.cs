namespace Rowles.Morphogenesis.Model;

public sealed class ContactEnergyMatrix
{
    private readonly double[] _values;

    public ContactEnergyMatrix(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int rows = values.GetLength(0);
        int columns = values.GetLength(1);
        if (rows == 0 || rows != columns)
        {
            throw new ArgumentException("The contact-energy matrix must be a non-empty square matrix.", nameof(values));
        }

        TypeCount = rows;
        _values = new double[checked(rows * rows)];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < rows; column++)
            {
                double value = values[row, column];
                if (!double.IsFinite(value))
                {
                    throw new ArgumentException("Contact energies must be finite.", nameof(values));
                }

                if (value != values[column, row])
                {
                    throw new ArgumentException("The contact-energy matrix must be symmetric.", nameof(values));
                }

                _values[row * rows + column] = value;
            }
        }
    }

    public int TypeCount { get; }

    public double this[int firstType, int secondType]
    {
        get
        {
            if ((uint)firstType >= (uint)TypeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(firstType));
            }

            if ((uint)secondType >= (uint)TypeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(secondType));
            }

            return _values[firstType * TypeCount + secondType];
        }
    }

    public double[,] ToArray()
    {
        double[,] result = new double[TypeCount, TypeCount];
        for (int row = 0; row < TypeCount; row++)
        {
            for (int column = 0; column < TypeCount; column++)
            {
                result[row, column] = _values[row * TypeCount + column];
            }
        }

        return result;
    }
}
