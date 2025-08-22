# BKDTree

BKDTree offers a simple and high performant implementation of a growing only `BKDTree<T>` and a static `KDTree<T>` for C# and .NET. 

A BKDTree and an KDTree allow storing any queying of multidimensional data. Non of these support a method for removing items.

For nearest neighbor queries there are dedicated variants like `MetricBKDTree<T>` and `MetricKDTree<T>`. As nearest neighbor queries require to calculate euclidean distance between values `T` must implement `IMetricTreeItem<in T>`.

![icon](https://raw.githubusercontent.com/DevAM-Tools/BKDTree/main/icon.png)

## Usage
`BKDTree<T>` or a `KDTree<T>` require a method `int CompareDimensionTo(T left, T right, int dimension)` for yout specific type `T`.

`MetricBKDTree<T>` and `MetricKDTree<T>` support nearest neighbor queries. Therefore a method `double GetDimension(T item, int dimension)` for the calculation of euclidean distances between values is required.

## Contribution

Contributions are welcome.

## License

MIT License

Copyright (c) 2024 DevAM

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
