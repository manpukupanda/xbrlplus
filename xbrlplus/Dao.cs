using HtmlAgilityPack;
using Manpuku.Edinet.Xbrl;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace xbrlplus;

internal static class Dao
{
	public static void CreateTable(SqliteConnection connection)
	{
		var command = connection.CreateCommand();
		command.CommandText =
		@"
        CREATE TABLE TDocuments (
            Id INTEGER PRIMARY KEY,
            Kind TEXT NOT NULL,
            Uri TEXT NOT NULL
        );

        CREATE TABLE TDocumentNodes (
            Id INTEGER PRIMARY KEY,
            DocumentId INTEGER NOT NULL,
            Depth INTEGER NOT NULL,
            ParentId INTEGER NULL
        );

        CREATE VIEW VDocumentNodes AS
        SELECT t.Id DocumentNodeId,
               t.ParentId,
               t.Depth,
               t.DocumentId,
               d.Kind,
               d.Uri
        FROM TDocumentNodes t
        JOIN TDocuments d ON t.DocumentId = d.Id;

        CREATE TABLE TConcepts (
            Id INTEGER PRIMARY KEY,
            Uri TEXT NOT NULL,
            NamespaceName TEXT NOT NULL,
            LocalName TEXT NOT NULL,
            TypeNS TEXT NULL,
  			TypeName TEXT NULL,
            Balance TEXT NOT NULL,
            Abstract TEXT NOT NULL,
            PeriodType TEXT NOT NULL,
            Nillable TEXT NOT NULL
        );

        CREATE TABLE TContexts (
            Id INTEGER PRIMARY KEY,
            Name TEXT NOT NULL,
            StartDate TEXT NULL,
            EndDate TEXT NULL,
            Instant TEXT NULL
        );

        CREATE TABLE TContextSenarios (
            Id INTEGER PRIMARY KEY,
            ContextId INTEGER NOT NULL,
            DimensionId INTEGER NOT NULL,
            MemberId INTEGER NOT NULL
        );

        CREATE VIEW VContextSenarios AS
        SELECT s.Id ContextSenarioId,
               cxt.Name ContextName,
               cpt1.NamespaceName DimensionNS,
               cpt1.LocalName DimensionName,
               cpt2.NamespaceName MemberNS,
               cpt2.LocalName MemberName
        FROM TContextSenarios s
        JOIN TContexts cxt ON s.ContextId = cxt.Id
        JOIN TConcepts cpt1 ON s.DimensionId = cpt1.Id
        JOIN TConcepts cpt2 ON s.MemberId = cpt2.Id;

        CREATE VIEW VContextDetails AS
        SELECT cxt.Id ContextId,
               cxt.Name ContextName,
               cxt.StartDate,
               cxt.EndDate,
               cxt.Instant,
               s.Id ContextSenarioId,
               cpt1.NamespaceName DimensionNS,
               cpt1.LocalName DimensionName,
               cpt2.NamespaceName MemberNS,
               cpt2.LocalName MemberName
        FROM TContexts cxt
        LEFT OUTER JOIN TContextSenarios s ON cxt.Id = s.ContextId
        LEFT OUTER JOIN TConcepts cpt1 ON s.DimensionId = cpt1.Id
        LEFT OUTER JOIN TConcepts cpt2 ON s.MemberId = cpt2.Id;

        CREATE TABLE TFacts (
            Id INTEGER PRIMARY KEY,
            ConceptId INTEGER NOT NULL,
            ContextId INTEGER NOT NULL,
            Nil INTEGER NOT NULL,
            Decimals INTEGER NULL,
            Unit TEXT NULL,
            Value TEXT NULL
        );

        CREATE VIEW VFacts AS
        SELECT f.Id FactId,
               cpt.NamespaceName,
               cpt.LocalName,
               f.Nil,
               f.Decimals,
               f.Unit,
               f.Value,
               cxt.Name ContextName,
               cxt.StartDate,
               cxt.EndDate,
               cxt.Instant
        FROM TFacts f
        JOIN TConcepts cpt ON f.ConceptId = cpt.Id
        JOIN TContexts cxt ON f.ContextId = cxt.Id;

        CREATE TABLE TLabels (
            Id INTEGER PRIMARY KEY,
            ConceptId INTEGER NOT NULL,
            Lang TEXT NOT NULL,
            Text TEXT NOT NULL,
            Role TEXT NOT NULL
        );

        CREATE VIEW VLabels AS
        SELECT l.Id LabelId,
               c.NamespaceName,
               c.LocalName,
               l.Lang,
               l.Text,
               l.Role
        FROM TLabels l
        JOIN TConcepts c ON l.ConceptId = c.Id;

        CREATE TABLE TReferences (
            Id INTEGER PRIMARY KEY,
            ConceptId INTEGER NOT NULL,
            RefNamespaceName TEXT NOT NULL,
            RefLocalName TEXT NOT NULL,
            RefValue TEXT NOT NULL,
            RefOrder INTEGER NOT NULL
        );

        CREATE VIEW VReferences AS
        SELECT r.Id ReferenceId,
               c.NamespaceName,
               c.LocalName,
               r.RefNamespaceName,
               r.RefLocalName,
               r.RefValue,
               r.RefOrder
        FROM TReferences r
        JOIN TConcepts c ON r.ConceptId = c.Id;

        CREATE TABLE TRoleTypes (
            Id INTEGER PRIMARY KEY,
            RoleURI TEXT NOT NULL,
            Definition TEXT NULL,
            DefinitionEn TEXT NULL
        );

        CREATE TABLE TLinkNodes (
            Id INTEGER PRIMARY KEY,
            RoleTypeId INTEGER NOT NULL,
            LinkType TEXT NOT NULL,
            Depth INTEGER NOT NULL,
            Seq INTEGER NOT NULL,
            ArcOrder REAL NULL,
            PreferredLabel TEXT NULL,
            Arcrole TEXT NULL,
            Weight INTEGER NULL,
            ParentId INTEGER NULL,
            ConceptId INTEGER NOT NULL
        );

        CREATE VIEW VPresentationLinkNodes AS
        SELECT lt.Id LinkNodeId,
               lt.ParentId,
               rt.RoleURI,
               rt.Definition,
               rt.DefinitionEn,
               lt.Depth,
               lt.Seq,
               lt.ArcOrder,
               lt.PreferredLabel,
               lt.ConceptId,
               cpt.LocalName
        FROM TLinkNodes lt
        JOIN TRoleTypes rt ON lt.RoleTypeId = rt.Id
        JOIN TConcepts cpt ON lt.ConceptId = cpt.Id
        WHERE lt.LinkType = 'PresentationLink';

        CREATE VIEW VDefinitionLinkNodes AS
        SELECT lt.Id LinkNodeId,
               lt.ParentId,
               rt.RoleURI,
               rt.Definition,
               rt.DefinitionEn,
               lt.Depth,
               lt.Seq,
               lt.ArcOrder,
               lt.Arcrole,
               lt.ConceptId,
               cpt.LocalName
        FROM TLinkNodes lt
        JOIN TRoleTypes rt ON lt.RoleTypeId = rt.Id
        JOIN TConcepts cpt ON lt.ConceptId = cpt.Id
        WHERE lt.LinkType = 'DefinitionLink';

        CREATE VIEW VCalclationLinkNodes AS
        SELECT lt.Id LinkNodeId,
               lt.ParentId,
               rt.RoleURI,
               rt.Definition,
               rt.DefinitionEn,
               lt.Depth,
               lt.Seq,
               lt.ArcOrder,
               lt.Weight,
               lt.ConceptId,
               cpt.LocalName
        FROM TLinkNodes lt
        JOIN TRoleTypes rt ON lt.RoleTypeId = rt.Id
        JOIN TConcepts cpt ON lt.ConceptId = cpt.Id
        WHERE lt.LinkType = 'CalclationLink';
        ";
		command.ExecuteNonQuery();
	}

	public static void SaveAll(SqliteConnection connection, DiscoverableTaxonomySet dts)
	{
		var documentIds = SaveDocuments(connection, dts);
		SaveDocumentNodes(connection, dts, documentIds);
		var conceptIds = SaveConcepts(connection, dts);
		var contextIds = SaveContexts(connection, dts, conceptIds);
		SaveFacts(connection, dts, conceptIds, contextIds);
		SaveLabels(connection, dts, conceptIds);
		SaveReferences(connection, dts, conceptIds);
		var roleTypeIds = SaveRoleTypes(connection, dts);
		var id = 1;
		id = SaveLinkNodes(connection, "PresentationLink", dts.PresentationLinkTrees, conceptIds, roleTypeIds, id);
		id = SaveLinkNodes(connection, "DefinitionLink", dts.DefinitionLinkTrees, conceptIds, roleTypeIds, id);
		SaveLinkNodes(connection, "CalclationLink", dts.CalculationLinkTrees, conceptIds, roleTypeIds, id);
	}

	private static Dictionary<XDocument, int> SaveDocuments(SqliteConnection connection, DiscoverableTaxonomySet dts)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TDocuments (Id, Kind, Uri) VALUES ($id, $kind, $uri)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var kindParam = command.CreateParameter();
		kindParam.ParameterName = "$kind";
		command.Parameters.Add(kindParam);

		var uriParam = command.CreateParameter();
		uriParam.ParameterName = "$uri";
		command.Parameters.Add(uriParam);

		int idCounter = 1;
		var ids = new Dictionary<XDocument, int>();
		foreach (var node in dts.DocumentTree.Nodes)
		{
			if (ids.ContainsKey(node.Document))
			{
				continue;
			}

			ids[node.Document] = idCounter;
			idParam.Value = idCounter++;
			kindParam.Value = node.NodeKind.ToString();
			uriParam.Value = node.URI.AbsoluteUri;
			command.ExecuteNonQuery();
		}

		return ids;
	}

	private static Dictionary<DocumentTreeNode, int> SaveDocumentNodes(SqliteConnection connection, DiscoverableTaxonomySet dts, Dictionary<XDocument, int> documentIds)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TDocumentNodes(Id, DocumentId, Depth, ParentId) VALUES ($id, $documentind, $depth, $parentid)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var documentIdParam = command.CreateParameter();
		documentIdParam.ParameterName = "$documentind";
		command.Parameters.Add(documentIdParam);

		var depthParam = command.CreateParameter();
		depthParam.ParameterName = "$depth";
		command.Parameters.Add(depthParam);

		var parentIdParam = command.CreateParameter();
		parentIdParam.ParameterName = "$parentid";
		command.Parameters.Add(parentIdParam);

		int idCounter = 1;
		var ids = new Dictionary<DocumentTreeNode, int>();
		foreach (var node in dts.DocumentTree.Nodes)
		{
			ids[node] = idCounter;
			idParam.Value = idCounter++;
			documentIdParam.Value = documentIds[node.Document];
			depthParam.Value = node.Distance;
			parentIdParam.Value = node.Parent == null ? (object)DBNull.Value : ids[node.Parent];
			command.ExecuteNonQuery();
		}

		return ids;
	}

	private static Dictionary<Concept, int> SaveConcepts(SqliteConnection connection, DiscoverableTaxonomySet dts)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TConcepts (Id, Uri, NamespaceName, LocalName, TypeNS, TypeName, Balance, Abstract, PeriodType, Nillable) VALUES ($id, $uri, $namespace, $localname, $typens, $typename, $balance, $absctract, $periodtype, $nillable)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var uriParam = command.CreateParameter();
		uriParam.ParameterName = "$uri";
		command.Parameters.Add(uriParam);

		var namespaceParam = command.CreateParameter();
		namespaceParam.ParameterName = "$namespace";
		command.Parameters.Add(namespaceParam);

		var localnameParam = command.CreateParameter();
		localnameParam.ParameterName = "$localname";
		command.Parameters.Add(localnameParam);

		var typeNsParam = command.CreateParameter();
		typeNsParam.ParameterName = "$typens";
		command.Parameters.Add(typeNsParam);

		var typeNameParam = command.CreateParameter();
		typeNameParam.ParameterName = "$typename";
		command.Parameters.Add(typeNameParam);

		var balanceParam = command.CreateParameter();
		balanceParam.ParameterName = "$balance";
		command.Parameters.Add(balanceParam);

		var abstractParam = command.CreateParameter();
		abstractParam.ParameterName = "$absctract";
		command.Parameters.Add(abstractParam);

		var periodTypeParam = command.CreateParameter();
		periodTypeParam.ParameterName = "$periodtype";
		command.Parameters.Add(periodTypeParam);

		var nillableParam = command.CreateParameter();
		nillableParam.ParameterName = "$nillable";
		command.Parameters.Add(nillableParam);

		int idCounter = 1;
		var conceptIds = new Dictionary<Concept, int>();
		foreach (var concept in dts.Concepts)
		{
			conceptIds[concept] = idCounter;
			idParam.Value = idCounter++;
			uriParam.Value = concept.URI!.AbsoluteUri + "#" + concept.Id;
			namespaceParam.Value = concept.Name.NamespaceName;
			localnameParam.Value = concept.Name.LocalName;
			typeNsParam.Value = concept.XBRLType?.NamespaceName ?? (object)DBNull.Value;
			typeNameParam.Value = concept.XBRLType?.LocalName ?? (object)DBNull.Value;
			balanceParam.Value = concept.Balance.ToString();
			abstractParam.Value = concept.Abstract.ToString();
			periodTypeParam.Value = concept.PeriodType.ToString();
			nillableParam.Value = concept.Nillable.ToString();

			command.ExecuteNonQuery();
		}

		return conceptIds;
	}

	private static Dictionary<Context, int> SaveContexts(SqliteConnection connection, DiscoverableTaxonomySet dts, Dictionary<Concept, int> conceptIds)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TContexts (Id, Name, StartDate, EndDate, Instant) VALUES ($id, $name, $startdate, $enddate, $instant)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var nameParam = command.CreateParameter();
		nameParam.ParameterName = "$name";
		command.Parameters.Add(nameParam);

		var startDateParam = command.CreateParameter();
		startDateParam.ParameterName = "$startdate";
		command.Parameters.Add(startDateParam);

		var endDateParam = command.CreateParameter();
		endDateParam.ParameterName = "$enddate";
		command.Parameters.Add(endDateParam);

		var instantParam = command.CreateParameter();
		instantParam.ParameterName = "$instant";
		command.Parameters.Add(instantParam);

		var commandSenario = connection.CreateCommand();
		commandSenario.CommandText = "INSERT INTO TContextSenarios (Id, ContextId, DimensionId, MemberId) VALUES ($id, $contextid, $dimensionid, $memberid)";

		var idParamSenario = commandSenario.CreateParameter();
		idParamSenario.ParameterName = "$id";
		commandSenario.Parameters.Add(idParamSenario);

		var contextIdParamSenario = commandSenario.CreateParameter();
		contextIdParamSenario.ParameterName = "$contextid";
		commandSenario.Parameters.Add(contextIdParamSenario);

		var dimensionIdParamSenario = commandSenario.CreateParameter();
		dimensionIdParamSenario.ParameterName = "$dimensionid";
		commandSenario.Parameters.Add(dimensionIdParamSenario);

		var memberIdParamSenario = commandSenario.CreateParameter();
		memberIdParamSenario.ParameterName = "$memberid";
		commandSenario.Parameters.Add(memberIdParamSenario);

		int idCounter = 1;
		int senarioIdCounter = 1;
		var ids = new Dictionary<Context, int>();

		foreach (var context in dts.Contexts)
		{
			ids[context] = idCounter;
			idParam.Value = idCounter++;
			nameParam.Value = context.Id;
			startDateParam.Value = context.StartDate == string.Empty ? (object)DBNull.Value : context.StartDate;
			endDateParam.Value = context.EndDate == string.Empty ? (object)DBNull.Value : context.EndDate;
			instantParam.Value = context.Instant == string.Empty ? (object)DBNull.Value : context.Instant;

			foreach (var dim in context.Scenario)
			{
				idParamSenario.Value = senarioIdCounter++;
				contextIdParamSenario.Value = ids[context];
				dimensionIdParamSenario.Value = conceptIds[dim.Dimension];
				memberIdParamSenario.Value = conceptIds[dim.Member];
				commandSenario.ExecuteNonQuery();
			}

			command.ExecuteNonQuery();
		}

		return ids;
	}

	private static Dictionary<Fact, int> SaveFacts(SqliteConnection connection, DiscoverableTaxonomySet dts, Dictionary<Concept, int> conceptIds, Dictionary<Context, int> contextIds)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TFacts (Id, ConceptId, ContextId, Nil, Decimals, Unit, Value) VALUES ($id, $conceptid, $contextid, $nil, $decimals, $unit, $value)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var conceptIdParam = command.CreateParameter();
		conceptIdParam.ParameterName = "$conceptid";
		command.Parameters.Add(conceptIdParam);

		var contextIdParam = command.CreateParameter();
		contextIdParam.ParameterName = "$contextid";
		command.Parameters.Add(contextIdParam);

		var nilParam = command.CreateParameter();
		nilParam.ParameterName = "$nil";
		command.Parameters.Add(nilParam);

		var decimalsParam = command.CreateParameter();
		decimalsParam.ParameterName = "$decimals";
		command.Parameters.Add(decimalsParam);

		var unitParam = command.CreateParameter();
		unitParam.ParameterName = "$unit";
		command.Parameters.Add(unitParam);

		var valueParam = command.CreateParameter();
		valueParam.ParameterName = "$value";
		command.Parameters.Add(valueParam);

		int idCounter = 1;
		var ids = new Dictionary<Fact, int>();

		foreach (var fact in dts.Facts)
		{
			ids[fact] = idCounter;
			idParam.Value = idCounter++;
			conceptIdParam.Value = conceptIds[fact.Concept];
			contextIdParam.Value = fact.Context != null ? contextIds[fact.Context] : (object)DBNull.Value;
			nilParam.Value = fact.Nil ? 1 : 0;
			decimalsParam.Value = fact.Decimals.HasValue ? (object)fact.Decimals : DBNull.Value;
			unitParam.Value = fact.Unit?.Id ?? (object)DBNull.Value;
			valueParam.Value = fact.Value ?? (object)DBNull.Value;
			command.ExecuteNonQuery();
		}
		return ids;
	}

	private static void SaveLabels(SqliteConnection connection, DiscoverableTaxonomySet dts, Dictionary<Concept, int> conceptIds)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TLabels (Id, ConceptId, Lang, Text, Role) VALUES ($id, $conceptid, $lang, $text, $role)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var conceptIdParam = command.CreateParameter();
		conceptIdParam.ParameterName = "$conceptid";
		command.Parameters.Add(conceptIdParam);

		var langParam = command.CreateParameter();
		langParam.ParameterName = "$lang";
		command.Parameters.Add(langParam);

		var textParam = command.CreateParameter();
		textParam.ParameterName = "$text";
		command.Parameters.Add(textParam);

		var roleParam = command.CreateParameter();
		roleParam.ParameterName = "$role";
		command.Parameters.Add(roleParam);

		int idCounter = 1;
		foreach (var concept in dts.Concepts)
		{
			if (dts.Labels.TryGetValue(concept, out var labels))
			{
				foreach (var label in labels)
				{
					idParam.Value = idCounter++;
					conceptIdParam.Value = conceptIds[concept];
					langParam.Value = label.Lang;
					textParam.Value = label.Value;
					roleParam.Value = label.Role;
					command.ExecuteNonQuery();
				}
			}
		}
	}

	private static void SaveReferences(SqliteConnection connection, DiscoverableTaxonomySet dts, Dictionary<Concept, int> conceptIds)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TReferences (Id, ConceptId, RefNamespaceName, RefLocalName, RefValue, RefOrder) VALUES ($id, $conceptid, $refnamespacename, $reflocalname, $refvalue, $reforder)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var conceptIdParam = command.CreateParameter();
		conceptIdParam.ParameterName = "$conceptid";
		command.Parameters.Add(conceptIdParam);

		var refNamespaceNameParam = command.CreateParameter();
		refNamespaceNameParam.ParameterName = "$refnamespacename";
		command.Parameters.Add(refNamespaceNameParam);

		var refLocalNameParam = command.CreateParameter();
		refLocalNameParam.ParameterName = "$reflocalname";
		command.Parameters.Add(refLocalNameParam);

		var refValueParam = command.CreateParameter();
		refValueParam.ParameterName = "$refvalue";
		command.Parameters.Add(refValueParam);

		var refOrderParam = command.CreateParameter();
		refOrderParam.ParameterName = "$reforder";
		command.Parameters.Add(refOrderParam);

		int idCounter = 1;
		foreach (var references in dts.References)
		{
			var concept = references.Key;
			var refs = references.Value;
			foreach (var reference in refs)
			{
				int order = 1;
				foreach (var refItem in reference.Ref)
				{
					idParam.Value = idCounter++;
					conceptIdParam.Value = conceptIds[concept];
					refOrderParam.Value = order++;
					refNamespaceNameParam.Value = refItem.name.NamespaceName;
					refLocalNameParam.Value = refItem.name.LocalName;
					refValueParam.Value = refItem.value;
					command.ExecuteNonQuery();
				}
			}
		}
	}

	private static Dictionary<RoleType, int> SaveRoleTypes(SqliteConnection connection, DiscoverableTaxonomySet dts)
	{
		var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO TRoleTypes (Id, RoleURI, Definition, DefinitionEn) VALUES ($id, $roleuri, $definition, $definitionen)";

		var idParam = command.CreateParameter();
		idParam.ParameterName = "$id";
		command.Parameters.Add(idParam);

		var roleUriParam = command.CreateParameter();
		roleUriParam.ParameterName = "$roleuri";
		command.Parameters.Add(roleUriParam);

		var definitionParam = command.CreateParameter();
		definitionParam.ParameterName = "$definition";
		command.Parameters.Add(definitionParam);

		var definitionEnParam = command.CreateParameter();
		definitionEnParam.ParameterName = "$definitionen";
		command.Parameters.Add(definitionEnParam);

		int idCounter = 1;
		var ids = new Dictionary<RoleType, int>();
		foreach (var roletype in dts.RoleTypes)
		{
			ids[roletype] = idCounter;
			idParam.Value = idCounter++;
			roleUriParam.Value = roletype.RoleURI;
			definitionParam.Value = roletype.Definition ?? (object)DBNull.Value;
			definitionEnParam.Value = roletype.DefinitionEn ?? (object)DBNull.Value;
			command.ExecuteNonQuery();
		}
		return ids;
	}

	private static int SaveLinkNodes(
		SqliteConnection connection,
		string linkType,
		IEnumerable<KeyValuePair<RoleType, LinkTree>> trees,
		Dictionary<Concept, int> conceptIds,
		Dictionary<RoleType, int> roleTypeIds,
		int id = 1)
	{
		var command = connection.CreateCommand();
		command.CommandText = $@"
        INSERT INTO TLinkNodes 
        (Id, RoleTypeId, LinkType, Depth, Seq, ArcOrder, PreferredLabel, Arcrole, Weight, ParentId, ConceptId) 
        VALUES 
        ($id, $roletypeid, $linktype, $depth, $seq, $order, $preferredlabel, $arcrole, $weight, $parentid, $conceptid)";

		var idParam = command.CreateParameter(); idParam.ParameterName = "$id"; command.Parameters.Add(idParam);
		var roleTypeIdParam = command.CreateParameter(); roleTypeIdParam.ParameterName = "$roletypeid"; command.Parameters.Add(roleTypeIdParam);
		var linkTypeParam = command.CreateParameter(); linkTypeParam.ParameterName = "$linktype"; command.Parameters.Add(linkTypeParam);
		var depthParam = command.CreateParameter(); depthParam.ParameterName = "$depth"; command.Parameters.Add(depthParam);
		var seqParam = command.CreateParameter(); seqParam.ParameterName = "$seq"; command.Parameters.Add(seqParam);
		var orderParam = command.CreateParameter(); orderParam.ParameterName = "$order"; command.Parameters.Add(orderParam);
		var preferredLabelParam = command.CreateParameter(); preferredLabelParam.ParameterName = "$preferredlabel"; command.Parameters.Add(preferredLabelParam);
		var arcroleParam = command.CreateParameter(); arcroleParam.ParameterName = "$arcrole"; command.Parameters.Add(arcroleParam);
		var weightParam = command.CreateParameter(); weightParam.ParameterName = "$weight"; command.Parameters.Add(weightParam);
		var parentIdParam = command.CreateParameter(); parentIdParam.ParameterName = "$parentid"; command.Parameters.Add(parentIdParam);
		var conceptIdParam = command.CreateParameter(); conceptIdParam.ParameterName = "$conceptid"; command.Parameters.Add(conceptIdParam);

		int idCounter = id;
		foreach (var tree in trees)
		{
			roleTypeIdParam.Value = roleTypeIds[tree.Key];
			linkTypeParam.Value = linkType;
			int seq = 1;
			var nodeIds = new Dictionary<LinkTree.Node, int>();

			foreach (var node in tree.Value.EnumerateDepthFirst())
			{
				nodeIds[node] = idCounter;
				idParam.Value = idCounter++;
				depthParam.Value = node.Distance;
				seqParam.Value = seq++;
				orderParam.Value = node.Order ?? (object)DBNull.Value;
				preferredLabelParam.Value = node.PreferredLabel ?? (object)DBNull.Value;
				arcroleParam.Value = node.Arcrole ?? (object)DBNull.Value;
				weightParam.Value = node.Weight ?? (object)DBNull.Value;
				parentIdParam.Value = node.Parent == null ? (object)DBNull.Value : nodeIds[node.Parent];
				conceptIdParam.Value = conceptIds[(Concept)node.Resource];
				command.ExecuteNonQuery();
			}
		}
		return idCounter;
	}



	public static class SqliteFunctions
	{
		public static bool MatchesRegex(string input, string pattern, bool caseSensitive)
		{
			if (input == null || pattern == null)
				return false;

			try
			{
				var options = caseSensitive == false ? RegexOptions.IgnoreCase : RegexOptions.None;
				return Regex.IsMatch(input, pattern, options);
			}
			catch (ArgumentException)
			{
				throw new SqliteException($"Invalid regex pattern: {pattern}", 1);
			}
		}

		public static string CleanHtmlText(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return "";

			bool looksLikeHtml = input.Contains("<") && input.Contains(">");

			string text = looksLikeHtml ? ExtractTextFromHtml(input) : input;

			// 空白正規化：&nbsp; や \t\n\r など → 半角スペース
			text = Regex.Replace(text, @"(&nbsp;|\s|\u00A0)+", " ");

			// 前後トリム
			return text.Trim();
		}

		private static string ExtractTextFromHtml(string html)
		{
			var doc = new HtmlDocument();
			doc.LoadHtml(html);

			var sb = new System.Text.StringBuilder();

			void AppendText(HtmlNode node)
			{
				if (node.NodeType == HtmlNodeType.Text)
				{
					sb.Append(node.InnerText);
				}
				else if (node.Name == "br")
				{
					sb.Append(" ");
				}
				else if (IsBlockTag(node.Name))
				{
					foreach (var child in node.ChildNodes)
						AppendText(child);
					sb.Append(" ");
				}
				else
				{
					foreach (var child in node.ChildNodes)
						AppendText(child);
				}
			}

			foreach (var node in doc.DocumentNode.ChildNodes)
				AppendText(node);

			return sb.ToString();
		}

		private static bool IsBlockTag(string tagName)
		{
			var blockTags = new[] { "div", "p", "section", "article", "header", "footer", "ul", "ol", "li", "table", "tr", "td" };
			return blockTags.Contains(tagName.ToLower());
		}

		public static string ExtractUriTail(string uri)
		{
			if (string.IsNullOrWhiteSpace(uri))
				return "";

			var parts = uri.TrimEnd('/').Split('/');
			return parts.Length > 0 ? parts[^1] : "";
		}
	}
}
