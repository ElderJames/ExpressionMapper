namespace ExpressionMapper
{
    using System.Collections;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Diagnostics;

    /// <summary>
    /// 表达式树实现的轻量Mapper.
    /// </summary>
    public static class ExpressionMapper
    {
        /// <summary>
        /// 映射对象到另一个类型.
        /// </summary>
        /// <typeparam name="TSource">源类型.</typeparam>
        /// <typeparam name="TTarget">目标类型.</typeparam>
        /// <param name="source">源对象.</param>
        /// <returns>目标类型的对象.</returns>
        public static TTarget Map<TSource, TTarget>(TSource source)
            where TSource : class
            where TTarget : class
        {
            return MapperInternal<TSource, TTarget>.Map(source);
        }

        /// <summary>
        /// 映射对象到另一个类型.
        /// </summary>
        /// <typeparam name="TSource">源类型.</typeparam>
        /// <typeparam name="TTarget">目标类型.</typeparam>
        /// <param name="source">源对象.</param>
        /// <param name="otherSetup">其他配置.</param>
        /// <returns>目标类型的对象.</returns>
        public static TTarget Map<TSource, TTarget>(TSource source, Action<TTarget> otherSetup)
            where TSource : class
            where TTarget : class
        {
            TTarget to = Map<TSource, TTarget>(source);
            otherSetup(to);
            return to;
        }

        /// <summary>
        /// 映射对象列表到另一个类型.
        /// </summary>
        /// <typeparam name="TSource">源类型.</typeparam>
        /// <typeparam name="TTarget">目标类型.</typeparam>
        /// <param name="sourceList">源对象列表.</param>
        /// <returns>目标类型的对象列表.</returns>
        public static IEnumerable<TTarget> Map<TSource, TTarget>(IEnumerable<TSource> sourceList)
            where TSource : class
            where TTarget : class
        {
            return MapperInternal<TSource, TTarget>.MapList(sourceList);
        }

        public static List<TTarget> Map<TSource, TTarget>(List<TSource> sourceList)
            where TSource : class
            where TTarget : class
        {
            return MapperInternal<TSource, TTarget>.MapList(sourceList).ToList();
        }

        public static TTarget[] Map<TSource, TTarget>(TSource[] sourceList)
            where TSource : class
            where TTarget : class
        {
            return MapperInternal<TSource, TTarget>.MapList(sourceList).ToArray();
        }

        public static void Map<TSource, TTarget>(TSource source, TTarget target)
            where TSource : class
            where TTarget : class
        {
            MapperInternal<TSource, TTarget>.Map(source, target);
        }

        private static class MapperInternal<TSource, TTarget>
            where TSource : class
            where TTarget : class
        {
            private static Func<TSource, TTarget> MapFunc { get; set; }

            private static Action<TSource, TTarget> MapAction { get; set; }

            /// <summary>
            /// 将对象TSource转换为TTarget.
            /// </summary>
            /// <param name="source">源类型对象.</param>
            /// <returns>返回目标类型的对象.</returns>
            public static TTarget Map(TSource source)
            {
                if (MapFunc == null)
                {
                    MapFunc = GetMapFunc();
                }

                return MapFunc(source);
            }

            public static IEnumerable<TTarget> MapList(IEnumerable<TSource> sources)
            {
                if (MapFunc == null)
                {
                    MapFunc = GetMapFunc();
                }

                return sources.Select(MapFunc);
            }

            /// <summary>
            /// 将对象TSource的值赋给给TTarget.
            /// </summary>
            /// <param name="source">源类型.</param>
            /// <param name="target">目标类型.</param>
            public static void Map(TSource source, TTarget target)
            {
                if (MapAction == null)
                {
                    MapAction = GetMapAction();
                }

                MapAction(source, target);
            }

            private static Func<TSource, TTarget> GetMapFunc()
            {
                var sourceType = typeof(TSource);
                var targetType = typeof(TTarget);

                if (IsEnumerable(sourceType) || IsEnumerable(targetType))
                {
                    throw new NotSupportedException("Enumerable types are not supported,please use MapList method.");
                }

                // Func委托传入变量
                var parameter = Expression.Parameter(sourceType, "p");

                var memberBindings = new List<MemberBinding>();
                var targetTypes = targetType.GetProperties().Where(x => x.CanWrite);
                foreach (var targetItem in targetTypes)
                {
                    var sourceItem = sourceType.GetProperty(targetItem.Name);

                    // 判断实体的读写权限
                    if (sourceItem == null || !sourceItem.CanRead || sourceItem.PropertyType.IsNotPublic)
                    {
                        continue;
                    }

                    // 标注NotMapped特性的属性忽略转换
                    if (sourceItem.GetCustomAttribute<NotMappedAttribute>() != null)
                    {
                        continue;
                    }

                    var sourceProperty = Expression.Property(parameter, sourceItem);

                    try
                    {
                        // 当非值类型且类型不相同时
                        if (!sourceItem.PropertyType.IsValueType && sourceItem.PropertyType != targetItem.PropertyType)
                        {
                            // 判断都是(非泛型、非数组)class
                            if (sourceItem.PropertyType.IsClass && targetItem.PropertyType.IsClass
                                && !sourceItem.PropertyType.IsArray && !targetItem.PropertyType.IsArray
                                && !sourceItem.PropertyType.IsGenericType && !targetItem.PropertyType.IsGenericType)
                            {
                                var expression = GetClassExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                                memberBindings.Add(Expression.Bind(targetItem, expression));
                            }

                            // 集合数组类型的转换
                            if (typeof(IEnumerable).IsAssignableFrom(sourceItem.PropertyType) && typeof(IEnumerable).IsAssignableFrom(targetItem.PropertyType))
                            {
                                var expression = GetListExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                                memberBindings.Add(Expression.Bind(targetItem, expression));
                            }

                            continue;
                        }

                        // 可空类型转换到非可空类型，当可空类型值为null时，用默认值赋给目标属性；不为null就直接转换
                        if (IsNullableType(sourceItem.PropertyType) && !IsNullableType(targetItem.PropertyType))
                        {
                            var hasValueExpression = Expression.Equal(Expression.Property(sourceProperty, "HasValue"), Expression.Constant(true));

                            // TODO: 枚举转字符串
                            var conditionItem = Expression.Condition(hasValueExpression, Expression.Convert(sourceProperty, targetItem.PropertyType), Expression.Default(targetItem.PropertyType));
                            memberBindings.Add(Expression.Bind(targetItem, conditionItem));

                            continue;
                        }

                        // 非可空类型转换到可空类型，直接转换
                        if (!IsNullableType(sourceItem.PropertyType) && IsNullableType(targetItem.PropertyType))
                        {
                            var memberExpression = Expression.Convert(sourceProperty, targetItem.PropertyType);
                            memberBindings.Add(Expression.Bind(targetItem, memberExpression));
                            continue;
                        }

                        if (targetItem.PropertyType != sourceItem.PropertyType)
                        {
                            continue;
                        }

                        memberBindings.Add(Expression.Bind(targetItem, sourceProperty));
                    }
                    catch (Exception ex)
                    {
                        memberBindings.Add(Expression.Bind(targetItem, Expression.Default(targetItem.PropertyType)));
                        Debug.Fail(ex.Message, ex.StackTrace);
                    }
                }

                // 创建一个if条件表达式
                var test = Expression.NotEqual(parameter, Expression.Constant(null, sourceType)); // p==null;
                var ifTrue = Expression.MemberInit(Expression.New(targetType), memberBindings);
                var condition = Expression.Condition(test, ifTrue, Expression.Constant(null, targetType));

                var lambda = Expression.Lambda<Func<TSource, TTarget>>(condition, parameter);
                return lambda.Compile();
            }

            /// <summary>
            /// 类型是class时赋值.
            /// </summary>
            /// <param name="sourceProperty">源类型的属性.</param>
            /// <param name="sourceType">源类型.</param>
            /// <param name="targetType">目标类型.</param>
            /// <returns>表达式树.</returns>
            private static Expression GetClassExpression(Expression sourceProperty, Type sourceType, Type targetType)
            {
                // 条件p.Item!=null
                var testItem = Expression.NotEqual(sourceProperty, Expression.Constant(null, sourceType));

                // 构造回调 Mapper<TSource, TTarget>.Map()
                var mapperType = typeof(MapperInternal<,>).MakeGenericType(sourceType, targetType);
                var ifTrue = Expression.Call(mapperType.GetMethod(nameof(Map), new[] { sourceType }) ?? throw new InvalidOperationException(), sourceProperty);

                var conditionItem = Expression.Condition(testItem, ifTrue, Expression.Constant(null, targetType));

                return conditionItem;
            }

            /// <summary>
            /// 类型为集合时赋值.
            /// </summary>
            /// <param name="sourceProperty">源类型的属性.</param>
            /// <param name="sourceType">源类型.</param>
            /// <param name="targetType">目标类型.</param>
            /// <returns>表达式树.</returns>
            private static Expression GetListExpression(Expression sourceProperty, Type sourceType, Type targetType)
            {
                // 条件p.Item!=null
                var testItem = Expression.NotEqual(sourceProperty, Expression.Constant(null, sourceType));

                // 构造回调 Mapper<TSource, TTarget>.MapList()
                var sourceArg = sourceType.IsArray ? sourceType.GetElementType() : sourceType.GetGenericArguments()[0];
                var targetArg = targetType.IsArray ? targetType.GetElementType() : targetType.GetGenericArguments()[0];
                var mapperType = typeof(MapperInternal<,>).MakeGenericType(sourceArg, targetArg);

                var mapperExecMap = Expression.Call(mapperType.GetMethod(nameof(MapList), new[] { sourceType }), sourceProperty);

                Expression ifTrue;
                if (targetType == mapperExecMap.Type)
                {
                    ifTrue = mapperExecMap;
                }
                else if (targetType.IsArray)
                {
                    // 数组类型调用ToArray()方法
                    ifTrue = Expression.Call(typeof(Enumerable), nameof(Enumerable.ToArray), new[] { mapperExecMap.Type.GenericTypeArguments[0] }, mapperExecMap);
                }
                else if (typeof(IDictionary).IsAssignableFrom(targetType))
                {
                    // 字典类型不转换
                    ifTrue = Expression.Constant(null, targetType);
                }
                else
                {
                    ifTrue = Expression.Convert(mapperExecMap, targetType);
                }

                var conditionItem = Expression.Condition(testItem, ifTrue, Expression.Constant(null, targetType));

                return conditionItem;
            }

            /// <summary>
            /// 源对象映射到目标对象.
            /// </summary>
            /// <returns>action.</returns>
            private static Action<TSource, TTarget> GetMapAction()
            {
                var sourceType = typeof(TSource);
                var targetType = typeof(TTarget);

                if (IsEnumerable(sourceType) || IsEnumerable(targetType))
                {
                    throw new NotSupportedException("Enumerable types are not supported,please use MapList method.");
                }

                // Func委托传入变量
                var sourceParameter = Expression.Parameter(sourceType, "p");
                var targetParameter = Expression.Parameter(targetType, "t");

                // 创建一个表达式集合
                var expressions = new List<Expression>();

                var targetTypes = targetType.GetProperties().Where(x => x.PropertyType.IsPublic && x.CanWrite);
                foreach (var targetItem in targetTypes)
                {
                    var sourceItem = sourceType.GetProperty(targetItem.Name);

                    // 判断实体的读写权限
                    if (sourceItem == null || !sourceItem.CanRead || sourceItem.PropertyType.IsNotPublic)
                    {
                        continue;
                    }

                    // 标注NotMapped特性的属性忽略转换
                    if (sourceItem.GetCustomAttribute<NotMappedAttribute>() != null)
                    {
                        continue;
                    }

                    var sourceProperty = Expression.Property(sourceParameter, sourceItem);
                    var targetProperty = Expression.Property(targetParameter, targetItem);

                    try
                    {
                        // 当非值类型且类型不相同时
                        if (!sourceItem.PropertyType.IsValueType && sourceItem.PropertyType != targetItem.PropertyType)
                        {
                            // 判断都是(非泛型、非数组)class
                            if (sourceItem.PropertyType.IsClass && targetItem.PropertyType.IsClass
                                && !sourceItem.PropertyType.IsArray && !targetItem.PropertyType.IsArray
                                && !sourceItem.PropertyType.IsGenericType && !targetItem.PropertyType.IsGenericType)
                            {
                                var expression = GetClassExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                                expressions.Add(Expression.Assign(targetProperty, expression));
                            }

                            // 集合数组类型的转换
                            if (typeof(IEnumerable).IsAssignableFrom(sourceItem.PropertyType) && typeof(IEnumerable).IsAssignableFrom(targetItem.PropertyType))
                            {
                                var expression = GetListExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                                expressions.Add(Expression.Assign(targetProperty, expression));
                            }

                            continue;
                        }

                        // 可空类型转换到非可空类型，当可空类型值为null时，用默认值赋给目标属性；不为null就直接转换
                        if (IsNullableType(sourceItem.PropertyType) && !IsNullableType(targetItem.PropertyType))
                        {
                            // if (p.xx.HasValue){
                            //     if (p.xx.Value != t.xx){
                            //      t.xx=p.Vale
                            //    }
                            //    else {
                            //        t.xx=default
                            //    }
                            // }
                            // else
                            //   t.xx=default
                            var hasValueExpression = Expression.Equal(Expression.Property(sourceProperty, "HasValue"), Expression.Constant(true));
                            var notEqualValueExpression = Expression.NotEqual(Expression.Property(sourceProperty, "Value"), targetProperty);
                            var notEqualCondition = Expression.Condition(notEqualValueExpression, Expression.Convert(sourceProperty, targetItem.PropertyType), Expression.Default(targetItem.PropertyType));

                            expressions.Add(Expression.IfThenElse(hasValueExpression, Expression.Assign(targetProperty, notEqualCondition), Expression.Assign(targetProperty, Expression.Default(targetItem.PropertyType))));
                            continue;
                        }

                        // 非可空类型转换到可空类型，直接转换
                        if (!IsNullableType(sourceItem.PropertyType) && IsNullableType(targetItem.PropertyType))
                        {
                            var hasValueExpression = Expression.Equal(Expression.Property(targetProperty, "HasValue"), Expression.Constant(true));
                            var notEqualValueExpression = Expression.And(hasValueExpression, Expression.NotEqual(sourceProperty, Expression.Property(targetProperty, "Value")));
                            var memberExpression = Expression.Convert(sourceProperty, targetItem.PropertyType);
                            expressions.Add(Expression.IfThen(notEqualValueExpression, Expression.Assign(targetProperty, memberExpression)));
                            continue;
                        }

                        if (targetItem.PropertyType != sourceItem.PropertyType)
                        {
                            continue;
                        }

                        var notEqualExpression = Expression.NotEqual(sourceProperty, targetProperty);
                        var conditionExpression = Expression.IfThen(notEqualExpression, Expression.Assign(targetProperty, sourceProperty));
                        expressions.Add(conditionExpression);
                    }
                    catch (Exception ex)
                    {
                        expressions.Add(Expression.Assign(targetProperty, Expression.Default(targetItem.PropertyType)));
                        Debug.Fail(ex.Message, ex.StackTrace);
                    }
                }

                // 当Target!=null判断source是否为空
                var testSource = Expression.NotEqual(sourceParameter, Expression.Constant(null, sourceType));
                var ifTrueSource = Expression.Block(expressions);
                var conditionSource = Expression.IfThen(testSource, ifTrueSource);

                // 判断target是否为空
                var testTarget = Expression.NotEqual(targetParameter, Expression.Constant(null, targetType));
                var conditionTarget = Expression.IfThen(testTarget, conditionSource);

                var lambda = Expression.Lambda<Action<TSource, TTarget>>(conditionTarget, sourceParameter, targetParameter);
                return lambda.Compile();
            }

            private static bool IsNullableType(Type type)
            {
                return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
            }

            private static bool IsEnumerable(Type type)
            {
                return type.IsArray || type.GetInterfaces().Any(x => x == typeof(ICollection) || x == typeof(IEnumerable));
            }
        }
    }
}