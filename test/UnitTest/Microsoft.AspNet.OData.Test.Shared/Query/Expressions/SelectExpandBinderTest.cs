﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNet.OData.Builder;
using Microsoft.AspNet.OData.Formatter;
using Microsoft.AspNet.OData.Query;
using Microsoft.AspNet.OData.Query.Expressions;
using Microsoft.AspNet.OData.Test.Abstraction;
using Microsoft.AspNet.OData.Test.Common;
using Microsoft.AspNet.OData.Test.Formatter.Serialization.Models;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Xunit;

namespace Microsoft.AspNet.OData.Test.Query.Expressions
{
    public class SelectExpandBinderTest
    {
        private readonly SelectExpandBinder _binder;
        private readonly CustomersModelWithInheritance _model;
        private readonly IQueryable<Customer> _queryable;
        private readonly ODataQueryContext _context;
        private readonly ODataQuerySettings _settings;

        public SelectExpandBinderTest()
        {
            _settings = new ODataQuerySettings { HandleNullPropagation = HandleNullPropagationOption.False };
            _model = new CustomersModelWithInheritance();
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.Customer, new ClrTypeAnnotation(typeof(Customer)));
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.SpecialCustomer, new ClrTypeAnnotation(typeof(SpecialCustomer)));
            _context = new ODataQueryContext(_model.Model, typeof(Customer)) { RequestContainer = new MockContainer() };
            _binder = new SelectExpandBinder(_settings, new SelectExpandQueryOption("*", "", _context));

            Customer customer = new Customer();
            Order order = new Order { Customer = customer };
            customer.Orders.Add(order);

            _queryable = new[] { customer }.AsQueryable();
        }

        [Fact]
        public void Bind_ReturnsIEdmObject_WithRightEdmType()
        {
            // Arrange
            SelectExpandQueryOption selectExpand = new SelectExpandQueryOption(select: "ID", expand: null, context: _context);

            // Act
            IQueryable queryable = SelectExpandBinder.Bind(_queryable, _settings, selectExpand);

            // Assert
            Assert.NotNull(queryable);
            IEdmTypeReference edmType = _model.Model.GetEdmTypeReference(queryable.GetType());
            Assert.NotNull(edmType);
            Assert.True(edmType.IsCollection());
            Assert.Same(_model.Customer, edmType.AsCollection().ElementType().Definition);
        }

        [Fact]
        public void Bind_GeneratedExpression_ContainsExpandedObject()
        {
            // Arrange
            SelectExpandQueryOption selectExpand = new SelectExpandQueryOption("Orders", "Orders,Orders($expand=Customer)", _context);
            IPropertyMapper mapper = new IdentityPropertyMapper();
            _model.Model.SetAnnotationValue(_model.Order, new DynamicPropertyDictionaryAnnotation(typeof(Order).GetProperty("OrderProperties")));

            // Act
            IQueryable queryable = SelectExpandBinder.Bind(_queryable, _settings, selectExpand);

            // Assert
            IEnumerator enumerator = queryable.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            var partialCustomer = Assert.IsAssignableFrom<SelectExpandWrapper<Customer>>(enumerator.Current);
            Assert.False(enumerator.MoveNext());
            Assert.Null(partialCustomer.Instance);
            Assert.Equal("NS.Customer", partialCustomer.InstanceType);
            IEnumerable<SelectExpandWrapper<Order>> innerOrders = partialCustomer.Container
                .ToDictionary(mapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(innerOrders);
            SelectExpandWrapper<Order> partialOrder = innerOrders.Single();
            Assert.Same(_queryable.First().Orders.First(), partialOrder.Instance);
            object customer = partialOrder.Container.ToDictionary(mapper)["Customer"];
            SelectExpandWrapper<Customer> innerInnerCustomer = Assert.IsAssignableFrom<SelectExpandWrapper<Customer>>(customer);
            Assert.Same(_queryable.First(), innerInnerCustomer.Instance);
        }

        //[Fact]
        //public void Bind_GeneratedExpression_CheckNullObjectWithinChainProjectionByKey()
        //{
        //    // Arrange
        //    SelectExpandQueryOption selectExpand = new SelectExpandQueryOption(null, "Orders($expand=Customer($select=City))", _context);
        //    _model.Model.SetAnnotationValue(_model.Order, new DynamicPropertyDictionaryAnnotation(typeof(Order).GetProperty("OrderProperties")));

        //    // Act
        //    IQueryable queryable = SelectExpandBinder.Bind(_queryable, _settings, selectExpand);

        //    // Assert
        //    var unaryExpression = (UnaryExpression)((MethodCallExpression)queryable.Expression).Arguments.Single(a => a is UnaryExpression);
        //    var expressionString = unaryExpression.Operand.ToString();
        //    Assert.Contains("IsNull = (Convert(Param_1.Customer.ID) == null)", expressionString);
        //}

        [Fact]
        public void ProjectAsWrapper_NonCollection_ContainsRightInstance()
        {
            // Arrange
            Order order = new Order();
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[0], allSelected: true);
            Expression source = Expression.Constant(order);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            SelectExpandWrapper<Order> projectedOrder = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Order>;
            Assert.NotNull(projectedOrder);
            Assert.Same(order, projectedOrder.Instance);
        }

        [Fact]
        public void ProjectAsWrapper_NonCollection_ProjectedValueNullAndHandleNullPropagationTrue()
        {
            // Arrange
            _settings.HandleNullPropagation = HandleNullPropagationOption.True;
            ExpandedNavigationSelectItem expandItem = new ExpandedNavigationSelectItem(
                new ODataExpandPath(new NavigationPropertySegment(_model.Order.NavigationProperties().Single(), navigationSource: _model.Customers)),
                _model.Customers,
                selectExpandOption: null);
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[] { expandItem }, allSelected: true);
            Expression source = Expression.Constant(null, typeof(Order));
            _model.Model.SetAnnotationValue(_model.Order, new DynamicPropertyDictionaryAnnotation(typeof(Order).GetProperty("OrderProperties")));

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            SelectExpandWrapper<Order> projectedOrder = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Order>;
            Assert.NotNull(projectedOrder);
            Assert.Null(projectedOrder.Instance);
            Assert.Null(projectedOrder.Container.ToDictionary(new IdentityPropertyMapper())["Customer"]);
        }

        [Fact]
        public void ProjectAsWrapper_NonCollection_ProjectedValueNullAndHandleNullPropagationFalse_Throws()
        {
            // Arrange
            _settings.HandleNullPropagation = HandleNullPropagationOption.False;
            ExpandedNavigationSelectItem expandItem = new ExpandedNavigationSelectItem(
                new ODataExpandPath(new NavigationPropertySegment(_model.Order.NavigationProperties().Single(), navigationSource: _model.Customers)),
                _model.Customers,
                selectExpandOption: null);
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[] { expandItem }, allSelected: true);
            _model.Model.SetAnnotationValue(_model.Order, new DynamicPropertyDictionaryAnnotation(typeof(Order).GetProperty("OrderProperties")));
            Expression source = Expression.Constant(null, typeof(Order));

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            var e = ExceptionAssert.Throws<TargetInvocationException>(
                () => Expression.Lambda(projection).Compile().DynamicInvoke());
            Assert.IsType<NullReferenceException>(e.InnerException);
        }

        [Fact]
        public void ProjectAsWrapper_Collection_ContainsRightInstance()
        {
            // Arrange
            Order[] orders = new Order[] { new Order() };
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[0], allSelected: true);
            Expression source = Expression.Constant(orders);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            IEnumerable<SelectExpandWrapper<Order>> projectedOrders = Expression.Lambda(projection).Compile().DynamicInvoke() as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(projectedOrders);
            Assert.Same(orders[0], projectedOrders.Single().Instance);
        }

        [Fact]
        public void ProjectAsWrapper_Collection_AppliesPageSize_AndOrderBy()
        {
            // Arrange
            int pageSize = 5;
            var orders = Enumerable.Range(0, 10).Select(i => new Order
            {
                ID = 10 - i,
            });
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[0], allSelected: true);
            Expression source = Expression.Constant(orders);
            _settings.PageSize = pageSize;

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            IEnumerable<SelectExpandWrapper<Order>> projectedOrders = Expression.Lambda(projection).Compile().DynamicInvoke() as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(projectedOrders);
            Assert.Equal(pageSize + 1, projectedOrders.Count());
            Assert.Equal(1, projectedOrders.First().Instance.ID);
        }

        [Fact]
        public void ProjectAsWrapper_ProjectionContainsExpandedProperties()
        {
            // Arrange
            Order order = new Order();
            ExpandedNavigationSelectItem expandItem = new ExpandedNavigationSelectItem(
                new ODataExpandPath(new NavigationPropertySegment(_model.Order.NavigationProperties().Single(), navigationSource: _model.Customers)),
                _model.Customers,
                selectExpandOption: null);
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[] { expandItem }, allSelected: true);
            Expression source = Expression.Constant(order);
            _model.Model.SetAnnotationValue(_model.Order, new DynamicPropertyDictionaryAnnotation(typeof(Order).GetProperty("OrderProperties")));

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            SelectExpandWrapper<Order> projectedOrder = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Order>;
            Assert.NotNull(projectedOrder);
            Assert.Contains("Customer", projectedOrder.Container.ToDictionary(new IdentityPropertyMapper()).Keys);
        }

        [Fact]
        public void ProjectAsWrapper_NullExpandedProperty_HasNullValueInProjectedWrapper()
        {
            // Arrange
            IPropertyMapper mapper = new IdentityPropertyMapper();
            Order order = new Order();
            ExpandedNavigationSelectItem expandItem = new ExpandedNavigationSelectItem(
                new ODataExpandPath(new NavigationPropertySegment(_model.Order.NavigationProperties().Single(), navigationSource: _model.Customers)),
                _model.Customers,
                selectExpandOption: null);
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[] { expandItem }, allSelected: true);
            Expression source = Expression.Constant(order);
            _model.Model.SetAnnotationValue(_model.Order, new DynamicPropertyDictionaryAnnotation(typeof(Order).GetProperty("OrderProperties")));

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            SelectExpandWrapper<Order> projectedOrder = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Order>;
            Assert.NotNull(projectedOrder);
            Assert.Contains("Customer", projectedOrder.Container.ToDictionary(mapper).Keys);
            Assert.Null(projectedOrder.Container.ToDictionary(mapper)["Customer"]);
        }

        [Fact]
        public void ProjectAsWrapper_Collection_ProjectedValueNullAndHandleNullPropagationTrue()
        {
            // Arrange
            _settings.HandleNullPropagation = HandleNullPropagationOption.True;
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[0], allSelected: true);
            Expression source = Expression.Constant(null, typeof(Order[]));

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            IEnumerable<SelectExpandWrapper<Order>> projectedOrders = Expression.Lambda(projection).Compile().DynamicInvoke() as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.Null(projectedOrders);
        }

        [Fact]
        public void ProjectAsWrapper_Collection_ProjectedValueNullAndHandleNullPropagationFalse_Throws()
        {
            // Arrange
            _settings.HandleNullPropagation = HandleNullPropagationOption.False;
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[0], allSelected: true);
            Expression source = Expression.Constant(null, typeof(Order[]));

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Order, _model.Orders);

            // Assert
            var e = ExceptionAssert.Throws<TargetInvocationException>(
                () => Expression.Lambda(projection).Compile().DynamicInvoke());
            Assert.IsType<ArgumentNullException>(e.InnerException);
        }

        [Fact]
        public void ProjectAsWrapper_Element_ProjectedValueContainsModelID()
        {
            // Arrange
            Customer customer = new Customer();
            SelectExpandClause selectExpand = new SelectExpandClause(new SelectItem[0], allSelected: true);
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;
            Assert.NotNull(customerWrapper.ModelID);
            Assert.Same(_model.Model, ModelContainer.GetModel(customerWrapper.ModelID));
        }

        [Fact]
        public void ProjectAsWrapper_Element_ProjectedValueContainsSubKeys_IfDollarRefInDollarExpand()
        {
            // Arrange
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.SpecialOrder, new ClrTypeAnnotation(typeof(SpecialOrder)));
            IPropertyMapper propertyMapper = new IdentityPropertyMapper();

            Customer customer = new Customer
            {
                Orders = new[]
                {
                    new Order { ID = 42 },
                    new SpecialOrder { ID = 38 }
                }
            };
            ODataQueryOptionParser parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$expand", "Orders/$ref" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            Assert.Equal(ExpressionType.MemberInit, projection.NodeType);
            Assert.NotEmpty((projection as MemberInitExpression).Bindings.Where(p => p.Member.Name == "Instance"));
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;

            var orders = customerWrapper.Container.ToDictionary(propertyMapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(orders);
            Assert.Equal(2, orders.Count());
            Assert.Equal(42, orders.ElementAt(0).Container.ToDictionary(propertyMapper)["ID"]);
            Assert.Equal(38, orders.ElementAt(1).Container.ToDictionary(propertyMapper)["ID"]);
        }

        [Fact]
        public void ProjectAsWrapper_Element_ProjectedValueContainsSubKeys_IfDollarRefInDollarExpand_AndNestedFilterClause()
        {
            // Arrange
            _model.Model.SetAnnotationValue(_model.Order, new ClrTypeAnnotation(typeof(Order)));
            _model.Model.SetAnnotationValue(_model.SpecialOrder, new ClrTypeAnnotation(typeof(SpecialOrder)));
            IPropertyMapper propertyMapper = new IdentityPropertyMapper();

            Customer customer = new Customer
            {
                Orders = new[]
                {
                    new Order { ID = 42, City = "xyz" },
                    new SpecialOrder { ID = 38, City = "abc" }
                }
            };
            ODataQueryOptionParser parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$expand", "Orders/$ref($filter=City eq 'abc')" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            Assert.Equal(ExpressionType.MemberInit, projection.NodeType);
            Assert.NotEmpty((projection as MemberInitExpression).Bindings.Where(p => p.Member.Name == "Instance"));
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;

            var orders = customerWrapper.Container.ToDictionary(propertyMapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(orders);
            var order = Assert.Single(orders); // only one
            Assert.Equal(38, order.Container.ToDictionary(propertyMapper)["ID"]);
        }

        [Fact]
        public void ProjectAsWrapper_Collection_ProjectedValueContainsSubKeys_IfDollarRefInDollarExpand()
        {
            // Arrange
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.SpecialOrder, new ClrTypeAnnotation(typeof(SpecialOrder)));
            IPropertyMapper propertyMapper = new IdentityPropertyMapper();

            Customer customer1 = new Customer
            {
                Orders = new[]
                {
                    new Order { ID = 8 },
                    new SpecialOrder { ID = 9 }
                }
            };
            Customer customer2 = new Customer
            {
                Orders = new[]
                {
                    new Order { ID = 18 },
                    new SpecialOrder { ID = 19 }
                }
            };

            ODataQueryOptionParser parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$expand", "Orders/$ref" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(new[] { customer1, customer2 });

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            Assert.Equal(ExpressionType.Call, projection.NodeType);
            var customerWrappers = Expression.Lambda(projection).Compile().DynamicInvoke() as IEnumerable<SelectExpandWrapper<Customer>>;
            Assert.Equal(2, customerWrappers.Count());

            var orders = customerWrappers.ElementAt(0).Container.ToDictionary(propertyMapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(orders);
            Assert.Equal(2, orders.Count());
            Assert.Equal(8, orders.ElementAt(0).Container.ToDictionary(propertyMapper)["ID"]);
            Assert.Equal(9, orders.ElementAt(1).Container.ToDictionary(propertyMapper)["ID"]);

            orders = customerWrappers.ElementAt(1).Container.ToDictionary(propertyMapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(orders);
            Assert.Equal(2, orders.Count());
            Assert.Equal(18, orders.ElementAt(0).Container.ToDictionary(propertyMapper)["ID"]);
            Assert.Equal(19, orders.ElementAt(1).Container.ToDictionary(propertyMapper)["ID"]);
        }

        [Fact]
        public void ProjectAsWrapper_Collection_ProjectedValueContainsSubKeys_IfDollarRefInDollarExpand_AndNestedTopAndSkip()
        {
            // Arrange
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.SpecialOrder, new ClrTypeAnnotation(typeof(SpecialOrder)));
            IPropertyMapper propertyMapper = new IdentityPropertyMapper();

            Customer customer1 = new Customer
            {
                Orders = new[]
                {
                    new Order { ID = 8 },
                    new SpecialOrder { ID = 9 }
                }
            };
            Customer customer2 = new Customer
            {
                Orders = new[]
                {
                    new Order { ID = 18 },
                    new SpecialOrder { ID = 19 }
                }
            };

            ODataQueryOptionParser parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$expand", "Orders/$ref($top=1;$skip=1)" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(new[] { customer1, customer2 });

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            Assert.Equal(ExpressionType.Call, projection.NodeType);
            var customerWrappers = Expression.Lambda(projection).Compile().DynamicInvoke() as IEnumerable<SelectExpandWrapper<Customer>>;
            Assert.Equal(2, customerWrappers.Count());

            var orders = customerWrappers.ElementAt(0).Container.ToDictionary(propertyMapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(orders);
            var order = Assert.Single(orders); // only one
            Assert.Equal(9, order.Container.ToDictionary(propertyMapper)["ID"]);

            orders = customerWrappers.ElementAt(1).Container.ToDictionary(propertyMapper)["Orders"] as IEnumerable<SelectExpandWrapper<Order>>;
            Assert.NotNull(orders);
            order = Assert.Single(orders);
            Assert.Equal(19, order.Container.ToDictionary(propertyMapper)["ID"]);
        }

        [Theory]
        [InlineData("*")]
        [InlineData("ID,*")]
        [InlineData("")]
        public void ProjectAsWrapper_Element_ProjectedValueContainsInstance_IfSelectionIsAll(string select)
        {
            // Arrange
            Customer customer = new Customer();
            ODataQueryOptionParser parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$select", select }, { "$expand", "Orders" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            Assert.Equal(ExpressionType.MemberInit, projection.NodeType);
            Assert.NotEmpty((projection as MemberInitExpression).Bindings.Where(p => p.Member.Name == "Instance"));
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;
            Assert.Same(customer, customerWrapper.Instance);
        }

        [Fact]
        public void ProjectAsWrapper_Element_ProjectedValueDoesNotContainInstance_IfSelectionIsPartial()
        {
            // Arrange
            Customer customer = new Customer();
            ODataQueryOptionParser parser = new ODataQueryOptionParser(_model.Model, _model.Customer, _model.Customers,
                new Dictionary<string, string> { { "$select", "ID,Orders" }, { "$expand", "Orders" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            Assert.Equal(ExpressionType.MemberInit, projection.NodeType);
            Assert.Empty((projection as MemberInitExpression).Bindings.Where(p => p.Member.Name == "Instance"));
            Assert.NotEmpty((projection as MemberInitExpression).Bindings.Where(p => p.Member.Name == "InstanceType"));
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;
            Assert.Null(customerWrapper.Instance);
            Assert.Equal("NS.Customer", customerWrapper.InstanceType);
        }

        [Fact]
        public void ProjectAsWrapper_Element_ProjectedValueContains_SelectedStructuralProperties()
        {
            // Arrange
            Customer customer = new Customer { Name = "OData" };
            ODataQueryOptionParser parser = new ODataQueryOptionParser(_model.Model, _model.Customer, _model.Customers,
                new Dictionary<string, string> { { "$select", "Name,Orders" }, { "$expand", "Orders" } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;
            Assert.Equal(customer.Name, customerWrapper.Container.ToDictionary(new IdentityPropertyMapper())["Name"]);
        }

        [Theory]
        [InlineData("Name")]
        [InlineData("NS.upgrade")]
        public void ProjectAsWrapper_Element_ProjectedValueContains_KeyPropertiesEvenIfNotPresentInSelectClause(string select)
        {
            // Arrange
            Customer customer = new Customer { ID = 42, FirstName = "OData" };
            ODataQueryOptionParser parser = new ODataQueryOptionParser(_model.Model, _model.Customer, _model.Customers,
                new Dictionary<string, string> { { "$select", select } });

            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;
            Assert.Equal(customer.ID, customerWrapper.Container.ToDictionary(new IdentityPropertyMapper())["ID"]);
        }

        [Theory]
        [InlineData("Name")]
        [InlineData("NS.upgrade")]
        public void ProjectAsWrapper_ProjectedValueContainsConcurrencyProperties_EvenIfNotPresentInSelectClause(string select)
        {
            // Arrange
            Customer customer = new Customer { ID = 42, City = "any" };

            ODataQueryOptionParser parser = new ODataQueryOptionParser(_model.Model, _model.Customer, _model.Customers,
                new Dictionary<string, string> { { "$select", select } });
            SelectExpandClause selectExpand = parser.ParseSelectAndExpand();
            Expression source = Expression.Constant(customer);

            // Act
            Expression projection = _binder.ProjectAsWrapper(source, selectExpand, _model.Customer, _model.Customers);

            // Assert
            SelectExpandWrapper<Customer> customerWrapper = Expression.Lambda(projection).Compile().DynamicInvoke() as SelectExpandWrapper<Customer>;
            Assert.Equal(customer.City, customerWrapper.Container.ToDictionary(new IdentityPropertyMapper())["City"]);
        }

        [Fact]
        public void CreatePropertyNameExpression_NonDerivedProperty_ReturnsConstantExpression()
        {
            Expression customer = Expression.Constant(new Customer());
            IEdmNavigationProperty ordersProperty = _model.Customer.NavigationProperties().Single();

            Expression property = _binder.CreatePropertyNameExpression(_model.Customer, ordersProperty, customer);

            Assert.Equal(ExpressionType.Constant, property.NodeType);
            Assert.Equal(ordersProperty.Name, (property as ConstantExpression).Value);
        }

        //[Fact]
        //public void CreatePropertyNameExpression_ThrowsODataException_IfMappingTypeIsNotFoundInModel()
        //{
        //    // Arrange
        //    _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.SpecialCustomer, null);

        //    Expression customer = Expression.Constant(new Customer());
        //    IEdmNavigationProperty specialOrdersProperty = _model.SpecialCustomer.DeclaredNavigationProperties().Single();

            //// Act & Assert
            //ExceptionAssert.Throws<ODataException>(
            //    () => _binder.CreatePropertyNameExpression(_model.Customer, specialOrdersProperty, customer),
            //    "The provided mapping does not contain a resource for the resource type 'NS.SpecialCustomer'.");
        //}

        [Fact]
        public void CreatePropertyNameExpression_DerivedProperty_ReturnsConditionalExpression()
        {
            Expression customer = Expression.Constant(new Customer());
            IEdmNavigationProperty specialOrdersProperty = _model.SpecialCustomer.DeclaredNavigationProperties().Single();

            Expression property = _binder.CreatePropertyNameExpression(_model.Customer, specialOrdersProperty, customer);

            Assert.Equal(ExpressionType.Conditional, property.NodeType);
            Assert.Equal(String.Format("IIF(({0} Is SpecialCustomer), \"SpecialOrders\", null)", customer.ToString()), property.ToString());
        }

        [Fact]
        public void CreatePropertyNameExpression_BaseProperty_From_DerivedType_ReturnsConstantExpression()
        {
            Expression customer = Expression.Constant(new SpecialCustomer());
            IEdmNavigationProperty ordersProperty = _model.Customer.NavigationProperties().Single();

            Expression property = _binder.CreatePropertyNameExpression(_model.SpecialCustomer, ordersProperty, customer);

            Assert.Equal(ExpressionType.Constant, property.NodeType);
            Assert.Equal(ordersProperty.Name, (property as ConstantExpression).Value);
        }

        [Fact]
        public void CreatePropertyValueExpression_NonDerivedProperty_ReturnsMemberAccessExpression()
        {
            Expression customer = Expression.Constant(new Customer());
            IEdmNavigationProperty ordersProperty = _model.Customer.NavigationProperties().Single();

            Expression property = _binder.CreatePropertyValueExpression(_model.Customer, ordersProperty, customer);

            Assert.Equal(ExpressionType.MemberAccess, property.NodeType);
            Assert.Equal(typeof(Customer).GetProperty("Orders"), (property as MemberExpression).Member);
        }

        //[Fact]
        //public void CreatePropertyValueExpression_ThrowsODataException_IfMappingTypeIsNotFoundInModel()
        //{
        //    // Arrange
        //    _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.SpecialCustomer, null);
        //    Expression customer = Expression.Constant(new Customer());
        //    IEdmNavigationProperty specialOrdersProperty = _model.SpecialCustomer.DeclaredNavigationProperties().Single();

        //    // Act & Assert
        //    ExceptionAssert.Throws<ODataException>(
        //        () => _binder.CreatePropertyValueExpression(_model.Customer, specialOrdersProperty, customer),
        //        "The provided mapping does not contain a resource for the resource type 'NS.SpecialCustomer'.");
        //}

        [Fact]
        public void CreatePropertyValueExpression_DerivedProperty_ReturnsPropertyAccessExpression()
        {
            Expression customer = Expression.Constant(new Customer());
            IEdmNavigationProperty specialOrdersProperty = _model.SpecialCustomer.DeclaredNavigationProperties().Single();

            Expression property = _binder.CreatePropertyValueExpression(_model.Customer, specialOrdersProperty, customer);

            Assert.Equal(String.Format("({0} As SpecialCustomer).SpecialOrders", customer.ToString()), property.ToString());
        }

        //[Fact]
        //public void CreatePropertyValueExpression_DerivedNonNullableProperty_ReturnsPropertyAccessExpressionCastToNullable()
        //{
        //    Expression customer = Expression.Constant(new Customer());
        //    IEdmStructuralProperty specialCustomerProperty = _model.SpecialCustomer.DeclaredStructuralProperties()
        //        .Single(s => s.Name == "SpecialCustomerProperty");

        //    Expression property = _binder.CreatePropertyValueExpression(_model.Customer, specialCustomerProperty, customer);

        //    Assert.Equal(
        //        String.Format("Convert(({0} As SpecialCustomer).SpecialCustomerProperty)", customer.ToString()),
        //        property.ToString());
        //}

        [Fact]
        public void CreatePropertyValueExpression_HandleNullPropagationTrue_AddsNullCheck()
        {
            _settings.HandleNullPropagation = HandleNullPropagationOption.True;
            Expression customer = Expression.Constant(new Customer());
            IEdmProperty idProperty = _model.Customer.StructuralProperties().Single(p => p.Name == "ID");

            Expression property = _binder.CreatePropertyValueExpression(_model.Customer, idProperty, customer);

            // NetFx and NetCore differ in the way Expression is converted to a string.
            Assert.Equal(ExpressionType.Conditional, property.NodeType);
            ConditionalExpression conditionalExpression = property as ConditionalExpression;
            Assert.NotNull(conditionalExpression);
            Assert.Equal(typeof(int?), conditionalExpression.Type);

            Assert.Equal(ExpressionType.Convert, conditionalExpression.IfFalse.NodeType);
            UnaryExpression falseUnaryExpression = conditionalExpression.IfFalse as UnaryExpression;
            Assert.NotNull(falseUnaryExpression);
            Assert.Equal(String.Format("{0}.ID", customer.ToString()), falseUnaryExpression.Operand.ToString());
            Assert.Equal(typeof(int?), falseUnaryExpression.Type);

            Assert.Equal(ExpressionType.Constant, conditionalExpression.IfTrue.NodeType);
            ConstantExpression trueUnaryExpression = conditionalExpression.IfTrue as ConstantExpression;
            Assert.NotNull(trueUnaryExpression);
            Assert.Equal("null", trueUnaryExpression.ToString());

            Assert.Equal(ExpressionType.Equal, conditionalExpression.Test.NodeType);
            BinaryExpression binaryExpression = conditionalExpression.Test as BinaryExpression;
            Assert.NotNull(binaryExpression);
            Assert.Equal(customer.ToString(), binaryExpression.Left.ToString());
            Assert.Equal("null", binaryExpression.Right.ToString());
            Assert.Equal(typeof(bool), binaryExpression.Type);
        }

        [Fact]
        public void CreatePropertyValueExpression_HandleNullPropagationFalse_ConvertsToNullableType()
        {
            _settings.HandleNullPropagation = HandleNullPropagationOption.False;
            Expression customer = Expression.Constant(new Customer());
            IEdmProperty idProperty = _model.Customer.StructuralProperties().Single(p => p.Name == "ID");

            Expression property = _binder.CreatePropertyValueExpression(_model.Customer, idProperty, customer);

#if NETCORE
            Assert.Equal(String.Format("Convert({0}.ID, Nullable`1)", customer.ToString()), property.ToString());
#else
            Assert.Equal(String.Format("Convert({0}.ID)", customer.ToString()), property.ToString());
#endif
            Assert.Equal(typeof(int?), property.Type);
        }

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Collection_ThrowsODataException_IfMappingTypeIsNotFoundInModel()
        {
            // Arrange
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.Order, value: null);
            var customer = Expression.Constant(new Customer());
            var ordersProperty = _model.Customer.NavigationProperties().Single(p => p.Name == "Orders");
            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Order,
                _model.Orders,
                new Dictionary<string, string> { { "$filter", "ID eq 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act & Assert
            ExceptionAssert.Throws<ODataException>(
                () => _binder.CreatePropertyValueExpressionWithFilter(_model.Customer, ordersProperty, customer, filterCaluse),
                "The provided mapping does not contain a resource for the resource type 'NS.Order'.");

            // NetFx and NetCore differ in the way Expression is converted to a string.
            //Assert.Equal(ExpressionType.Convert, property.NodeType);
            //var unaryExpression = (property as UnaryExpression);
            //Assert.NotNull(unaryExpression);
            //Assert.Equal(String.Format("{0}.ID", customer.ToString()), unaryExpression.Operand.ToString());
            //Assert.Equal(typeof(int?), unaryExpression.Type);
        }

        //[Fact]
        //public void CreatePropertyValueExpressionWithFilter_ThrowsODataException_IfMappingTypeIsNotFoundInModel()
        //{
        //    // Arrange
        //    _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.Order, value: null);
        //    var customer = Expression.Constant(new Customer());
        //    var ordersProperty = _model.Customer.NavigationProperties().Single(p => p.Name == "Orders");
        //    var parser = new ODataQueryOptionParser(
        //        _model.Model,
        //        _model.Order,
        //        _model.Orders,
        //        new Dictionary<string, string> { { "$filter", "ID eq 1" } });
        //    var filterCaluse = parser.ParseFilter();

        //    // Act & Assert
        //    ExceptionAssert.Throws<ODataException>(
        //        () => _binder.CreatePropertyValueExpressionWithFilter(_model.Customer, ordersProperty, customer, filterCaluse),
        //        "The provided mapping does not contain a resource for the resource type 'NS.Order'.");
        //}

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Collection_Works_HandleNullPropagationOptionIsTrue()
        {
            // Arrange
            _model.Model.SetAnnotationValue(_model.Order, new ClrTypeAnnotation(typeof(Order)));
            _settings.HandleNullPropagation = HandleNullPropagationOption.True;
            var customer =
                Expression.Constant(new Customer { Orders = new[] { new Order { ID = 1 }, new Order { ID = 2 } } });
            var ordersProperty = _model.Customer.NavigationProperties().Single(p => p.Name == "Orders");
            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Order,
                _model.Orders,
                new Dictionary<string, string> { { "$filter", "ID eq 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act
            var filterInExpand = _binder.CreatePropertyValueExpressionWithFilter(
                _model.Customer,
                ordersProperty,
                customer,
                filterCaluse);

            // Assert
            Assert.Equal(
                string.Format(
                    "IIF((value({0}) == null), null, IIF((value({0}).Orders == null), null, " +
                    "value({0}).Orders.Where($it => ($it.ID == value({1}).TypedProperty))))",
                    customer.Type,
                    "Microsoft.AspNet.OData.Query.Expressions.LinqParameterContainer+TypedLinqParameterContainer`1[System.Int32]"),
                filterInExpand.ToString());
            var orders = Expression.Lambda(filterInExpand).Compile().DynamicInvoke() as IEnumerable<Order>;
            Assert.Single(orders);
            Assert.Equal(1, orders.ToList()[0].ID);
        }

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Collection_Works_HandleNullPropagationOptionIsFalse()
        {
            // Arrange
            _model.Model.SetAnnotationValue(_model.Order, new ClrTypeAnnotation(typeof(Order)));
            _settings.HandleNullPropagation = HandleNullPropagationOption.False;
            var customer =
                Expression.Constant(new Customer { Orders = new[] { new Order { ID = 1 }, new Order { ID = 2 } } });
            var ordersProperty = _model.Customer.NavigationProperties().Single(p => p.Name == "Orders");
            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Order,
                _model.Orders,
                new Dictionary<string, string> { { "$filter", "ID eq 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act
            var filterInExpand = _binder.CreatePropertyValueExpressionWithFilter(
                _model.Customer,
                ordersProperty,
                customer,
                filterCaluse);

            // Assert
            Assert.Equal(
                string.Format(
                    "value({0}).Orders.Where($it => ($it.ID == value(" +
                    "Microsoft.AspNet.OData.Query.Expressions.LinqParameterContainer+TypedLinqParameterContainer`1[System.Int32]).TypedProperty))",
                    customer.Type),
                filterInExpand.ToString());
            var orders = Expression.Lambda(filterInExpand).Compile().DynamicInvoke() as IEnumerable<Order>;
            Assert.Single(orders);
            Assert.Equal(1, orders.ToList()[0].ID);
        }

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Single_ThrowsODataException_IfMappingTypeIsNotFoundInModel()
        {
            // Arrange
            _model.Model.SetAnnotationValue<ClrTypeAnnotation>(_model.Customer, value: null);
            _settings.HandleReferenceNavigationPropertyExpandFilter = true;
            var order = Expression.Constant(new Order());
            var customerProperty = _model.Order.NavigationProperties().Single(p => p.Name == "Customer");

            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$filter", "ID eq 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act & Assert
            ExceptionAssert.Throws<ODataException>(
                () => _binder.CreatePropertyValueExpressionWithFilter(_model.Order, customerProperty, order, filterCaluse),
                "The provided mapping does not contain a resource for the resource type 'NS.Customer'.");
        }

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Single_Works_IfSettingIsOff()
        {
            // Arrange
            _settings.HandleReferenceNavigationPropertyExpandFilter = false;
            var order = Expression.Constant(
                    new Order
                    {
                        Customer = new Customer
                        {
                            ID = 1
                        }
                    }
            );
            var customerProperty = _model.Order.NavigationProperties().Single(p => p.Name == "Customer");

            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$filter", "ID ne 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act 
            var filterInExpand = _binder.CreatePropertyValueExpressionWithFilter(_model.Order, customerProperty, order, filterCaluse);

            // Assert            
            var customer = Expression.Lambda(filterInExpand).Compile().DynamicInvoke() as Customer;
            Assert.NotNull(customer);
            Assert.Equal(1, customer.ID);
        }

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Single_Works_HandleNullPropagationOptionIsTrue()
        {
            // Arrange
            _settings.HandleReferenceNavigationPropertyExpandFilter = true;
            _settings.HandleNullPropagation = HandleNullPropagationOption.True;
            var order = Expression.Constant(
                    new Order
                    {
                        Customer = new Customer
                        {
                            ID = 1
                        }
                    }
            );
            var customerProperty = _model.Order.NavigationProperties().Single(p => p.Name == "Customer");

            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$filter", "ID ne 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act
            var filterInExpand = _binder.CreatePropertyValueExpressionWithFilter(_model.Order, customerProperty, order, filterCaluse);
            
            // Assert
            Assert.Equal(
                string.Format(
                    "IIF((value({0}) == null), null, IIF((value({0}).Customer == null), null, " +
                    "IIF((value({0}).Customer.ID != value(Microsoft.AspNet.OData.Query.Expressions.LinqParameterContainer+TypedLinqParameterContainer`1[System.Int32]).TypedProperty), " +
                    "value({0}).Customer, null)))",
                    order.Type),
                filterInExpand.ToString());
            var customer = Expression.Lambda(filterInExpand).Compile().DynamicInvoke() as Customer;
            Assert.Null(customer);
        }

        [Fact]
        public void CreatePropertyValueExpressionWithFilter_Single_Works_HandleNullPropagationOptionIsFalse()
        {
            // Arrange
            _settings.HandleReferenceNavigationPropertyExpandFilter = true;
            _settings.HandleNullPropagation = HandleNullPropagationOption.False;
            var order = Expression.Constant(
                    new Order
                    {
                        Customer = new Customer
                        {
                            ID = 1
                        }
                    }
            );
            var customerProperty = _model.Order.NavigationProperties().Single(p => p.Name == "Customer");

            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$filter", "ID ne 1" } });
            var filterCaluse = parser.ParseFilter();

            // Act
            var filterInExpand = _binder.CreatePropertyValueExpressionWithFilter(_model.Order, customerProperty, order, filterCaluse);

            // Assert
            Assert.Equal(
                string.Format(
                    "IIF((value({0}).Customer.ID != value(Microsoft.AspNet.OData.Query.Expressions.LinqParameterContainer+TypedLinqParameterContainer`1[System.Int32]).TypedProperty), " +
                    "value({0}).Customer, null)",
                    order.Type),
                filterInExpand.ToString());
            var customer = Expression.Lambda(filterInExpand).Compile().DynamicInvoke() as Customer;
            Assert.Null(customer);
        }

        [Fact]
        public void CreateTypeNameExpression_ReturnsNull_IfTypeHasNoDerivedTypes()
        {
            // Arrange
            IEdmEntityType baseType = new EdmEntityType("NS", "BaseType");
            EdmModel model = new EdmModel();
            model.AddElement(baseType);

            Expression source = Expression.Constant(42);

            // Act
            Expression result = SelectExpandBinder.CreateTypeNameExpression(source, baseType, model);

            // Assert
            Assert.Null(result);
        }

        //[Fact]
        //public void CreateTypeNameExpression_ThrowsODataException_IfTypeHasNoMapping()
        //{
        //    // Arrange
        //    IEdmEntityType baseType = new EdmEntityType("NS", "BaseType");
        //    IEdmEntityType derivedType = new EdmEntityType("NS", "DerivedType", baseType);
        //    EdmModel model = new EdmModel();
        //    model.AddElement(baseType);
        //    model.AddElement(derivedType);

        //    Expression source = Expression.Constant(42);

        //    // Act & Assert
        //    ExceptionAssert.Throws<ODataException>(
        //        () => SelectExpandBinder.CreateTypeNameExpression(source, baseType, model),
        //        "The provided mapping does not contain a resource for the resource type 'NS.DerivedType'.");
        //}

        [Fact]
        public void CreateTypeNameExpression_ReturnsConditionalExpression_IfTypeHasDerivedTypes()
        {
            // Arrange
            IEdmEntityType baseType = new EdmEntityType("NS", "BaseType");
            IEdmEntityType typeA = new EdmEntityType("NS", "A", baseType);
            IEdmEntityType typeB = new EdmEntityType("NS", "B", baseType);
            IEdmEntityType typeAA = new EdmEntityType("NS", "AA", typeA);
            IEdmEntityType typeAAA = new EdmEntityType("NS", "AAA", typeAA);
            IEdmEntityType[] types = new[] { baseType, typeA, typeAAA, typeB, typeAA };

            EdmModel model = new EdmModel();
            foreach (var type in types)
            {
                model.AddElement(type);
                model.SetAnnotationValue(type, new ClrTypeAnnotation(new MockType(type.Name, @namespace: type.Namespace)));
            }

            Expression source = Expression.Constant(42);

            // Act
            Expression result = SelectExpandBinder.CreateTypeNameExpression(source, baseType, model);

            // Assert
            Assert.Equal(
                @"IIF((42 Is AAA), ""NS.AAA"", IIF((42 Is AA), ""NS.AA"", IIF((42 Is B), ""NS.B"", IIF((42 Is A), ""NS.A"", ""NS.BaseType""))))",
                result.ToString());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CorrelatedSubqueryIncludesToListIfBufferingOptimizationIsTrue(bool enableOptimization)
        {
            // Arrange
            _settings.EnableCorrelatedSubqueryBuffering = enableOptimization;
            var customer =
                Expression.Constant(new Customer { Orders = new[] { new Order { ID = 1 }, new Order { ID = 2 } } });
            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$expand", "Orders" } });
            var expandClause = parser.ParseSelectAndExpand();

            // Act
            var expand = _binder.ProjectAsWrapper(
                customer,
                expandClause,
                _model.Customer,
                _model.Customers);

            // Assert
            Assert.True(expand.ToString().Contains("ToList") == enableOptimization);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CorrelatedSubqueryIncludesToListIfBufferingOptimizationIsTrueAndPagesizeIsSet(bool enableOptimization)
        {
            // Arrange
            _settings.EnableCorrelatedSubqueryBuffering = enableOptimization;
            _settings.PageSize = 100;
            var customer =
                Expression.Constant(new Customer { Orders = new[] { new Order { ID = 1 }, new Order { ID = 2 } } });
            var parser = new ODataQueryOptionParser(
                _model.Model,
                _model.Customer,
                _model.Customers,
                new Dictionary<string, string> { { "$expand", "Orders" } });
            var expandClause = parser.ParseSelectAndExpand();

            // Act
            var expand = _binder.ProjectAsWrapper(
                customer,
                expandClause,
                _model.Customer,
                _model.Customers);

            // Assert
            Assert.True(expand.ToString().Contains("ToList") == enableOptimization);
        }

        private class SpecialCustomer : Customer
        {
            public int SpecialCustomerProperty { get; set; }

            public SpecialOrder[] SpecialOrders { get; set; }
        }

        private class SpecialOrder : Order
        {
            public SpecialCustomer[] SpecialCustomers { get; set; }
        }
    }
}
