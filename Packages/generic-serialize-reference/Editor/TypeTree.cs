using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;

namespace GenericSerializeReference
{

    /// <summary>
    ///
    /// create tree structure for types
    ///
    /// "X": class
    /// "<-": inherit from
    /// "X(I)": X implement interface I
    ///
    ///   A <-+-- B(I) <---- C
    ///       |
    ///       +-- D(I) <-+-- E(I)
    ///                 |
    ///                 +-- F
    ///
    /// type tree structure: A ( B ( C ) + D ( E + F ) )
    /// interface chain: I(B, D, E)
    /// </summary>
    public class TypeTree
    {
        class TypeDefinitionTokenComparer : IEqualityComparer<TypeDefinition>
        {
            public bool Equals(TypeDefinition x, TypeDefinition y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.MetadataToken.Equals(y.MetadataToken);
            }

            public int GetHashCode(TypeDefinition obj)
            {
                return obj.MetadataToken.GetHashCode();
            }
        }

        private class TypeTreeNode
        {
            public ISet<TypeDefinition> Interfaces { get; } = new HashSet<TypeDefinition>(new TypeDefinitionTokenComparer());
            public TypeDefinition Type { get; }
            public TypeTreeNode Base { get; set; }
            public List<TypeTreeNode> Subs = new List<TypeTreeNode>();

            public TypeTreeNode(TypeDefinition type) => Type = type;
        }

        private readonly Dictionary<MetadataToken, TypeTreeNode> _typeTreeNodeMap;
        private readonly Dictionary<MetadataToken, TypeTreeNode> _interfaceImplementations;

        /// <summary>
        /// Create a type-tree from a collection of <paramref name="sourceTypes"/>
        /// </summary>
        /// <param name="sourceTypes">The source types of tree.</param>
        public TypeTree([NotNull] IEnumerable<TypeDefinition> sourceTypes)
        {
            _typeTreeNodeMap = new Dictionary<MetadataToken, TypeTreeNode>();
            _interfaceImplementations = new Dictionary<MetadataToken, TypeTreeNode>();
            foreach (var type in sourceTypes) CreateTypeTree(type);
        }

        // TODO:
        // /// <summary>
        // /// Create a type-tree from a collection of <paramref name="sourceTypes"/>
        // /// </summary>
        // /// <param name="sourceTypes">The source types of tree.</param>
        // /// <param name="baseTypes">Excluded any types from <paramref name="sourceTypes"/> which is not derived from base type.</param>
        // public TypeTree([NotNull] ICollection<TypeDefinition> sourceTypes, [NotNull] ICollection<TypeDefinition> baseTypes)
        // {
        // }

        /// <summary>
        /// Get all derived class type of <paramref name="baseType"/>.
        /// Ignore generic type argument if <paramref name="baseType"/> is a generic class with certain type argument.
        /// </summary>
        /// <param name="baseType"></param>
        /// <returns>Any type of classes derived from <paramref name="baseType"/> directly or indirectly.</returns>
        /// <exception cref="ArgumentException"></exception>
        public IEnumerable<TypeDefinition> GetDerived(TypeDefinition baseType)
        {
            TypeTreeNode node = null;
            if (baseType.IsInterface) _interfaceImplementations.TryGetValue(baseType.MetadataToken, out node);
            else _typeTreeNodeMap.TryGetValue(baseType.MetadataToken, out node);

            if (node == null) throw new ArgumentException($"{baseType} is not part of this tree");
            return GetDescendantsAndSelf(node).Skip(1).Select(n => n.Type);

            IEnumerable<TypeTreeNode> GetDescendantsAndSelf(TypeTreeNode node)
            {
                yield return node;
                foreach (var childNode in
                    from sub in node.Subs
                    from type in GetDescendantsAndSelf(sub)
                    select type
                ) yield return childNode;
            }
        }

        TypeTreeNode CreateTypeTree(TypeDefinition type)
        {
            if (type == null) return null;
            if (_typeTreeNodeMap.TryGetValue(type.MetadataToken, out var node)) return node;

            var self = new TypeTreeNode(type);
            foreach (var @interface in type.Interfaces)
                self.Interfaces.Add(@interface.InterfaceType.Resolve());

            var parent = CreateTypeTree(type.BaseType?.Resolve());
            var uniqueInterfaces = new HashSet<TypeDefinition>(self.Interfaces, new TypeDefinitionTokenComparer());
            if (parent != null)
            {
                self.Base = parent;
                parent.Subs.Add(self);
                self.Interfaces.UnionWith(parent.Interfaces);
                uniqueInterfaces.ExceptWith(parent.Interfaces);
            }

            foreach (var @interface in uniqueInterfaces)
            {
                var token = @interface.MetadataToken;
                if (!_interfaceImplementations.TryGetValue(token, out var implementations))
                {
                    implementations = new TypeTreeNode(@interface);
                    _interfaceImplementations.Add(token, implementations);
                }
                implementations.Subs.Add(self);
            }
            _typeTreeNodeMap.Add(type.MetadataToken, self);
            return self;
        }
    }
}