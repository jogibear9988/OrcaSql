using System;
using System.Collections.ObjectModel;

namespace OrcaSql.Core.MetaData
{
	/// <summary>
	/// Stores the actual data contained in a row, including a reference to the row schema.
	/// </summary>
	public abstract class Row
	{
		protected ISchema Schema;
		private object[] values;

		public ReadOnlyCollection<DataColumn> Columns => Schema.Columns;

        protected Row()
        {
        }

        protected Row(ISchema schema)
		{
			Schema = schema;
			values = new object[schema.Columns.Count];
		}

		private int GetOrdinal(string name)
		{
			if(Schema == null || !Schema.TryGetOrdinal(name, out var ordinal))
				throw new ArgumentOutOfRangeException("Column '" + name + "' does not exist.");

			EnsureValueStorage();
			return ordinal;
		}

		private void EnsureValueStorage()
		{
			if (values == null && Schema != null)
				values = new object[Schema.Columns.Count];
		}

		public T Field<T>(DataColumn col)
		{
			return Field<T>(col.Name);
		}

		public T Field<T>(string name)
		{
			var ordinal = GetOrdinal(name);
			var value = values[ordinal];

			// We need to handle nullables explicitly
			var t = typeof (T);
			var u = Nullable.GetUnderlyingType(t);
			
			if(u != null)
			{
				if (value == null)
					return default(T);

				return (T)Convert.ChangeType(value, u);
			}

			return (T)Convert.ChangeType(value, t);
		}

		public object this[string name]
		{
			get
			{
				return values[GetOrdinal(name)];
            }
			set
			{
				values[GetOrdinal(name)] = value;
			}
		}

		public object this[DataColumn col]
		{
			get => this[col.Name];
            set => this[col.Name] = value;
        }

		internal void SetValueUnchecked(DataColumn col, object value)
		{
			var ordinal = col.Ordinal;
			if (ordinal < 0 || ordinal >= Schema.Columns.Count || Schema.Columns[ordinal].Name != col.Name)
				ordinal = GetOrdinal(col.Name);
			else
				EnsureValueStorage();

			values[ordinal] = value;
		}

		public abstract Row NewRow();
	}
}
