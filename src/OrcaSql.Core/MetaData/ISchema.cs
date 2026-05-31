using System.Collections.ObjectModel;

namespace OrcaSql.Core.MetaData
{
	public interface ISchema
	{
		ReadOnlyCollection<DataColumn> Columns { get; }
		bool HasColumn(string name);
		bool TryGetOrdinal(string name, out int ordinal);
	}
}
