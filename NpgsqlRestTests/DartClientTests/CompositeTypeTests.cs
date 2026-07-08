namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientCompositeTypeTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Composite type for nested JSON tests
create type dartclient_test.dart_nested_address as (
    street text,
    city text,
    zip_code text
);

-- Composite type with various field types
create type dartclient_test.dart_person_info as (
    id int,
    name text,
    age int,
    is_active boolean,
    score numeric
);

-- Function returning table with composite type column (nested JSON)
create function dartclient_test.get_users_with_address()
returns table(
    user_id int,
    user_name text,
    address dartclient_test.dart_nested_address
)
language sql as
$$
select * from (values
    (1, 'Alice', row('123 Main St', 'New York', '10001')::dartclient_test.dart_nested_address),
    (2, 'Bob', row('456 Oak Ave', 'Boston', '02101')::dartclient_test.dart_nested_address)
) as t(user_id, user_name, address);
$$;
comment on function dartclient_test.get_users_with_address() is '
dartclient_module=dart_composite_types
nested
';

-- Function returning array of composite type
create function dartclient_test.get_authors_with_books()
returns table(
    author_id int,
    author_name text,
    books dartclient_test.dart_person_info[]
)
language sql as
$$
select * from (values
    (1, 'Author One', array[
        row(1, 'Book 1', 100, true, 9.5)::dartclient_test.dart_person_info,
        row(2, 'Book 2', 200, false, 8.0)::dartclient_test.dart_person_info
    ])
) as t(author_id, author_name, books);
$$;
comment on function dartclient_test.get_authors_with_books() is '
dartclient_module=dart_composite_types
';

-- Function with both: composite column AND array of composite
create function dartclient_test.get_complex_data()
returns table(
    id int,
    main_address dartclient_test.dart_nested_address,
    contacts dartclient_test.dart_person_info[]
)
language sql as
$$
select
    1,
    row('789 Pine Rd', 'Chicago', '60601')::dartclient_test.dart_nested_address,
    array[
        row(1, 'Contact 1', 30, true, 7.5)::dartclient_test.dart_person_info,
        row(2, 'Contact 2', 25, false, 8.5)::dartclient_test.dart_person_info
    ];
$$;
comment on function dartclient_test.get_complex_data() is '
dartclient_module=dart_composite_types
nested
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class CompositeTypeTests
    {
        private const string Expected = """
import 'dart:convert';
import 'package:http/http.dart' as http;

String baseUrl = '';

/// Override to inject a custom [http.Client] (e.g. MockClient in tests).
http.Client? httpClient;

http.Client get _client => httpClient ??= http.Client();

Future<http.Response> _send(
  String method,
  Uri uri, {
  Map<String, String>? headers,
  String? body,
}) async {
  final request = http.Request(method, uri);
  if (headers != null) {
    request.headers.addAll(headers);
  }
  if (body != null) {
    request.body = body;
  }
  return http.Response.fromStream(await _client.send(request));
}

class Books {
  final int? id;
  final String? name;
  final int? age;
  final bool? isActive;
  final double? score;

  const Books({
    this.id,
    this.name,
    this.age,
    this.isActive,
    this.score,
  });

  factory Books.fromJson(Map<String, dynamic> json) {
    return Books(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
      age: (json['age'] as num?)?.toInt(),
      isActive: json['isActive'] as bool?,
      score: (json['score'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'age': age,
      'isActive': isActive,
      'score': score,
    };
  }
}

class MainAddress {
  final String? street;
  final String? city;
  final String? zipCode;

  const MainAddress({
    this.street,
    this.city,
    this.zipCode,
  });

  factory MainAddress.fromJson(Map<String, dynamic> json) {
    return MainAddress(
      street: json['street'] as String?,
      city: json['city'] as String?,
      zipCode: json['zipCode'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'street': street,
      'city': city,
      'zipCode': zipCode,
    };
  }
}

class DartclientTestGetAuthorsWithBooksResponse {
  final int? authorId;
  final String? authorName;
  final List<Books>? books;

  const DartclientTestGetAuthorsWithBooksResponse({
    this.authorId,
    this.authorName,
    this.books,
  });

  factory DartclientTestGetAuthorsWithBooksResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetAuthorsWithBooksResponse(
      authorId: (json['authorId'] as num?)?.toInt(),
      authorName: json['authorName'] as String?,
      books: (json['books'] as List?)?.map((e) => Books.fromJson(e as Map<String, dynamic>)).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'authorId': authorId,
      'authorName': authorName,
      'books': books?.map((e) => e.toJson()).toList(),
    };
  }
}

class DartclientTestGetComplexDataResponse {
  final int? id;
  final MainAddress? mainAddress;
  final List<Books>? contacts;

  const DartclientTestGetComplexDataResponse({
    this.id,
    this.mainAddress,
    this.contacts,
  });

  factory DartclientTestGetComplexDataResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetComplexDataResponse(
      id: (json['id'] as num?)?.toInt(),
      mainAddress: json['mainAddress'] == null ? null : MainAddress.fromJson(json['mainAddress'] as Map<String, dynamic>),
      contacts: (json['contacts'] as List?)?.map((e) => Books.fromJson(e as Map<String, dynamic>)).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'mainAddress': mainAddress?.toJson(),
      'contacts': contacts?.map((e) => e.toJson()).toList(),
    };
  }
}

class DartclientTestGetUsersWithAddressResponse {
  final int? userId;
  final String? userName;
  final MainAddress? address;

  const DartclientTestGetUsersWithAddressResponse({
    this.userId,
    this.userName,
    this.address,
  });

  factory DartclientTestGetUsersWithAddressResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetUsersWithAddressResponse(
      userId: (json['userId'] as num?)?.toInt(),
      userName: json['userName'] as String?,
      address: json['address'] == null ? null : MainAddress.fromJson(json['address'] as Map<String, dynamic>),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'userId': userId,
      'userName': userName,
      'address': address?.toJson(),
    };
  }
}

/// function dartclient_test.get_authors_with_books()
/// returns table(
///     author_id integer,
///     author_name text,
///     books dartclient_test.dart_person_info[]
/// )
///
/// comment on function dartclient_test.get_authors_with_books is 'dartclient_module=dart_composite_types';
///
/// Returns `List<DartclientTestGetAuthorsWithBooksResponse>`.
///
/// See FUNCTION dartclient_test.get_authors_with_books
Future<List<DartclientTestGetAuthorsWithBooksResponse>> dartclientTestGetAuthorsWithBooks() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-authors-with-books');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGetAuthorsWithBooksResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// function dartclient_test.get_complex_data()
/// returns table(
///     id integer,
///     main_address text,
///     main_address text,
///     main_address text,
///     contacts dartclient_test.dart_person_info[]
/// )
///
/// comment on function dartclient_test.get_complex_data is 'dartclient_module=dart_composite_types
/// nested';
///
/// Returns `List<DartclientTestGetComplexDataResponse>`.
///
/// See FUNCTION dartclient_test.get_complex_data
Future<List<DartclientTestGetComplexDataResponse>> dartclientTestGetComplexData() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-complex-data');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGetComplexDataResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// function dartclient_test.get_users_with_address()
/// returns table(
///     user_id integer,
///     user_name text,
///     address text,
///     address text,
///     address text
/// )
///
/// comment on function dartclient_test.get_users_with_address is 'dartclient_module=dart_composite_types
/// nested';
///
/// Returns `List<DartclientTestGetUsersWithAddressResponse>`.
///
/// See FUNCTION dartclient_test.get_users_with_address
Future<List<DartclientTestGetUsersWithAddressResponse>> dartclientTestGetUsersWithAddress() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-users-with-address');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGetUsersWithAddressResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_CompositeTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_composite_types.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
