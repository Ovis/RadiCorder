using System.Reflection;

namespace RadiCorder.Logics.Primitives
{
    /// <summary>
    /// 列挙型クラス 基底
    /// https://docs.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/enumeration-classes-over-enum-types
    /// </summary>
    public abstract class Enumeration(int id, string name) : IComparable
    {
        public int Id { get; } = id;
        public string Name { get; private set; } = name;


        public override string ToString() => Name;

        public static IEnumerable<T> GetAll<T>() where T : Enumeration =>
            typeof(T).GetFields(BindingFlags.Public |
                                BindingFlags.Static |
                                BindingFlags.DeclaredOnly)
                .Select(f => f.GetValue(null))
                .Cast<T>();

        public override bool Equals(object? obj)
        {
            if (obj is not Enumeration otherValue)
            {
                return false;
            }

            var typeMatches = GetType() == obj.GetType();
            var valueMatches = Id.Equals(otherValue.Id);

            return typeMatches && valueMatches;
        }

        protected bool Equals(Enumeration other) => Id == other.Id;

        public int CompareTo(object? other) => Id.CompareTo(((Enumeration)other!).Id);

        public override int GetHashCode() => Id.GetHashCode();



        public static bool operator ==(Enumeration? left, Enumeration? right)
        {
            if (left is null && right is not null)
                return false;

            if (left is not null && right is null)
                return false;

            if (left is null && right is null)
                return true;

            return (left!.Equals(right!));
        }

        public static bool operator !=(Enumeration? left, Enumeration? right)
        {
            return !(left == right);
        }

        public static bool operator <(Enumeration? left, Enumeration? right)
        {
            if (left is null)
                throw new ArgumentNullException(nameof(left));

            if (right is null)
                throw new ArgumentNullException(nameof(right));

            return (left.CompareTo(right) < 0);
        }

        public static bool operator >(Enumeration? left, Enumeration? right)
        {
            return (right < left);
        }

        public static bool operator <=(Enumeration? left, Enumeration? right)
        {
            if (left is null)
                throw new ArgumentNullException(nameof(left));

            if (right is null)
                throw new ArgumentNullException(nameof(right));

            return (left.CompareTo(right) <= 0);
        }

        public static bool operator >=(Enumeration? left, Enumeration? right)
        {
            return (right <= left);
        }
    }
}
