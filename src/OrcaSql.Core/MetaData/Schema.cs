using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrcaSql.Core.MetaData
{
	public class Schema : ISchema
	{
		private readonly List<DataColumn> _columns = new List<DataColumn>();
		private readonly Dictionary<string, int> _columnOrdinals = new Dictionary<string, int>();
		private readonly ReadOnlyCollection<DataColumn> _readOnlyColumns;
		
		public Schema(IEnumerable<DataColumn> columns)
		{
			_columns.AddRange(columns);
			_readOnlyColumns = _columns.AsReadOnly();

			for (var i = 0; i < _columns.Count; i++)
			{
				_columns[i].Ordinal = i;
				_columnOrdinals[_columns[i].Name] = i;
			}
		}

		public ReadOnlyCollection<DataColumn> Columns => _readOnlyColumns;

        public bool HasColumn(string name)
		{
			return _columnOrdinals.ContainsKey(name);
		}

		public bool TryGetOrdinal(string name, out int ordinal)
		{
			return _columnOrdinals.TryGetValue(name, out ordinal);
		}
	}
}
