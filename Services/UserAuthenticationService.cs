using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public sealed class UserAuthenticationService
    {
        private const int PasswordIterationCount = 120000;
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private readonly object databaseSync = new object();
        private readonly string connectionString;

        public UserAuthenticationService(string? databasePath = null)
        {
            DatabasePath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Data",
                "app-users.db");

            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }

        public string DatabasePath { get; }

        public bool TryInitialize(
            out bool createdDefaultUsers,
            out string? errorMessage)
        {
            createdDefaultUsers = false;

            try
            {
                lock (databaseSync)
                {
                    string? directory = Path.GetDirectoryName(DatabasePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        PRAGMA journal_mode = WAL;
                        PRAGMA busy_timeout = 3000;

                        CREATE TABLE IF NOT EXISTS app_users
                        (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            normalized_username TEXT NOT NULL UNIQUE,
                            username TEXT NOT NULL,
                            display_name TEXT NOT NULL,
                            role TEXT NOT NULL,
                            password_salt BLOB NOT NULL,
                            password_hash BLOB NOT NULL,
                            password_iterations INTEGER NOT NULL,
                            is_enabled INTEGER NOT NULL DEFAULT 1,
                            must_change_password INTEGER NOT NULL DEFAULT 1,
                            created_at_utc TEXT NOT NULL,
                            last_login_at_utc TEXT NULL
                        );

                        PRAGMA user_version = 1;
                        ";
                    command.ExecuteNonQuery();

                    using SqliteCommand countCommand =
                        connection.CreateCommand();
                    countCommand.CommandText =
                        "SELECT COUNT(*) FROM app_users;";
                    long userCount =
                        (long)(countCommand.ExecuteScalar() ?? 0L);
                    if (userCount == 0)
                    {
                        using SqliteTransaction transaction =
                            connection.BeginTransaction();
                        InsertUser(
                            connection,
                            transaction,
                            "operator",
                            "生产操作员",
                            UserRole.Operator,
                            "Operator@123");
                        InsertUser(
                            connection,
                            transaction,
                            "engineer",
                            "视觉工程师",
                            UserRole.Engineer,
                            "Engineer@123");
                        InsertUser(
                            connection,
                            transaction,
                            "admin",
                            "系统管理员",
                            UserRole.Administrator,
                            "Admin@123");
                        transaction.Commit();
                        createdDefaultUsers = true;
                    }
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                createdDefaultUsers = false;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryAuthenticate(
            string username,
            string password,
            out AuthenticatedUser? authenticatedUser,
            out string? errorMessage)
        {
            authenticatedUser = null;
            string normalizedUsername = NormalizeUsername(username);
            if (normalizedUsername.Length == 0 || string.IsNullOrEmpty(password))
            {
                errorMessage = "用户名或密码错误。";
                return false;
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        SELECT
                            id,
                            username,
                            display_name,
                            role,
                            password_salt,
                            password_hash,
                            password_iterations,
                            is_enabled,
                            must_change_password
                        FROM app_users
                        WHERE normalized_username = $normalizedUsername;
                        ";
                    command.Parameters.AddWithValue(
                        "$normalizedUsername",
                        normalizedUsername);

                    using SqliteDataReader reader = command.ExecuteReader();
                    if (!reader.Read())
                    {
                        errorMessage = "用户名或密码错误。";
                        return false;
                    }

                    byte[] salt = (byte[])reader[4];
                    byte[] storedHash = (byte[])reader[5];
                    int iterations = reader.GetInt32(6);
                    bool isEnabled = reader.GetInt64(7) == 1;
                    byte[] candidateHash = HashPassword(
                        password,
                        salt,
                        iterations);
                    bool passwordMatches =
                        CryptographicOperations.FixedTimeEquals(
                            candidateHash,
                            storedHash);
                    CryptographicOperations.ZeroMemory(candidateHash);

                    if (!isEnabled || !passwordMatches)
                    {
                        errorMessage = "用户名或密码错误。";
                        return false;
                    }

                    if (!Enum.TryParse(
                            reader.GetString(3),
                            true,
                            out UserRole role))
                    {
                        errorMessage = "用户角色配置无效，请联系管理员。";
                        return false;
                    }

                    authenticatedUser = new AuthenticatedUser(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        role,
                        reader.GetInt64(8) == 1);
                    reader.Close();

                    using SqliteCommand updateCommand =
                        connection.CreateCommand();
                    updateCommand.CommandText =
                        @"
                        UPDATE app_users
                        SET last_login_at_utc = $lastLoginAtUtc
                        WHERE id = $id;
                        ";
                    updateCommand.Parameters.AddWithValue(
                        "$lastLoginAtUtc",
                        DateTime.UtcNow.ToString("O"));
                    updateCommand.Parameters.AddWithValue(
                        "$id",
                        authenticatedUser.Id);
                    updateCommand.ExecuteNonQuery();
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                authenticatedUser = null;
                errorMessage = $"登录验证失败：{ex.Message}";
                return false;
            }
        }

        public bool TryChangePassword(
            long userId,
            string currentPassword,
            string newPassword,
            out AuthenticatedUser? updatedUser,
            out string? errorMessage)
        {
            updatedUser = null;
            if (userId <= 0)
            {
                errorMessage = "当前用户会话无效，请重新登录。";
                return false;
            }

            if (string.IsNullOrEmpty(currentPassword))
            {
                errorMessage = "请输入当前密码。";
                return false;
            }

            if (!TryValidateNewPassword(newPassword, out errorMessage))
            {
                return false;
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteTransaction transaction =
                        connection.BeginTransaction();
                    using SqliteCommand queryCommand =
                        connection.CreateCommand();
                    queryCommand.Transaction = transaction;
                    queryCommand.CommandText =
                        @"
                        SELECT
                            username,
                            display_name,
                            role,
                            password_salt,
                            password_hash,
                            password_iterations,
                            is_enabled
                        FROM app_users
                        WHERE id = $id;
                        ";
                    queryCommand.Parameters.AddWithValue("$id", userId);

                    string username;
                    string displayName;
                    UserRole role;
                    byte[] oldSalt;
                    byte[] oldHash;
                    int oldIterations;
                    using (SqliteDataReader reader =
                        queryCommand.ExecuteReader())
                    {
                        if (!reader.Read() || reader.GetInt64(6) != 1)
                        {
                            errorMessage = "当前用户不存在或已被禁用。";
                            return false;
                        }

                        username = reader.GetString(0);
                        displayName = reader.GetString(1);
                        if (!Enum.TryParse(
                                reader.GetString(2),
                                true,
                                out role))
                        {
                            errorMessage = "用户角色配置无效，请联系管理员。";
                            return false;
                        }

                        oldSalt = (byte[])reader[3];
                        oldHash = (byte[])reader[4];
                        oldIterations = reader.GetInt32(5);
                    }

                    byte[] currentCandidate = HashPassword(
                        currentPassword,
                        oldSalt,
                        oldIterations);
                    bool currentPasswordMatches =
                        CryptographicOperations.FixedTimeEquals(
                            currentCandidate,
                            oldHash);
                    CryptographicOperations.ZeroMemory(currentCandidate);
                    CryptographicOperations.ZeroMemory(oldHash);
                    if (!currentPasswordMatches)
                    {
                        errorMessage = "当前密码错误。";
                        return false;
                    }

                    if (string.Equals(
                            currentPassword,
                            newPassword,
                            StringComparison.Ordinal))
                    {
                        errorMessage = "新密码不能与当前密码相同。";
                        return false;
                    }

                    byte[] newSalt =
                        RandomNumberGenerator.GetBytes(SaltSizeBytes);
                    byte[] newHash = HashPassword(
                        newPassword,
                        newSalt,
                        PasswordIterationCount);
                    try
                    {
                        using SqliteCommand updateCommand =
                            connection.CreateCommand();
                        updateCommand.Transaction = transaction;
                        updateCommand.CommandText =
                            @"
                            UPDATE app_users
                            SET password_salt = $passwordSalt,
                                password_hash = $passwordHash,
                                password_iterations = $passwordIterations,
                                must_change_password = 0
                            WHERE id = $id;
                            ";
                        updateCommand.Parameters.Add(
                            "$passwordSalt",
                            SqliteType.Blob).Value = newSalt;
                        updateCommand.Parameters.Add(
                            "$passwordHash",
                            SqliteType.Blob).Value = newHash;
                        updateCommand.Parameters.AddWithValue(
                            "$passwordIterations",
                            PasswordIterationCount);
                        updateCommand.Parameters.AddWithValue("$id", userId);
                        if (updateCommand.ExecuteNonQuery() != 1)
                        {
                            throw new InvalidDataException(
                                "密码更新没有影响预期的用户记录。");
                        }

                        transaction.Commit();
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(newHash);
                    }

                    updatedUser = new AuthenticatedUser(
                        userId,
                        username,
                        displayName,
                        role,
                        false);
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                updatedUser = null;
                errorMessage = $"密码更新失败：{ex.Message}";
                return false;
            }
        }

        public bool TryGetUsers(
            AuthenticatedUser administrator,
            out IReadOnlyList<UserAccountSummary> users,
            out string? errorMessage)
        {
            try
            {
                var result = new List<UserAccountSummary>();
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    if (!TryVerifyAdministrator(
                            connection,
                            administrator,
                            out errorMessage))
                    {
                        users = Array.Empty<UserAccountSummary>();
                        return false;
                    }

                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText =
                        @"
                        SELECT id, username, display_name, role, is_enabled,
                               must_change_password, created_at_utc,
                               last_login_at_utc
                        FROM app_users
                        ORDER BY id;
                        ";
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!Enum.TryParse(
                                reader.GetString(3),
                                true,
                                out UserRole role))
                        {
                            continue;
                        }

                        result.Add(new UserAccountSummary
                        {
                            Id = reader.GetInt64(0),
                            Username = reader.GetString(1),
                            DisplayName = reader.GetString(2),
                            Role = role,
                            IsEnabled = reader.GetInt64(4) == 1,
                            MustChangePassword = reader.GetInt64(5) == 1,
                            CreatedAtUtc = DateTime.Parse(
                                reader.GetString(6),
                                null,
                                System.Globalization.DateTimeStyles.RoundtripKind),
                            LastLoginAtUtc = reader.IsDBNull(7)
                                ? null
                                : DateTime.Parse(
                                    reader.GetString(7),
                                    null,
                                    System.Globalization.DateTimeStyles.RoundtripKind)
                        });
                    }
                }

                users = result;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                users = Array.Empty<UserAccountSummary>();
                errorMessage = $"用户列表读取失败：{ex.Message}";
                return false;
            }
        }

        public bool TryCreateUser(
            AuthenticatedUser administrator,
            string username,
            string displayName,
            UserRole role,
            string initialPassword,
            out string? errorMessage)
        {
            if (!TryValidateUsername(username, out errorMessage) ||
                !TryValidateDisplayName(displayName, out errorMessage) ||
                !Enum.IsDefined(typeof(UserRole), role) ||
                !TryValidateNewPassword(initialPassword, out errorMessage))
            {
                errorMessage ??= "用户角色无效。";
                return false;
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    if (!TryVerifyAdministrator(
                            connection,
                            administrator,
                            out errorMessage))
                    {
                        return false;
                    }

                    using SqliteTransaction transaction =
                        connection.BeginTransaction();
                    InsertUser(
                        connection,
                        transaction,
                        username.Trim(),
                        displayName.Trim(),
                        role,
                        initialPassword);
                    transaction.Commit();
                }

                errorMessage = null;
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                errorMessage = "该用户名已经存在。";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"用户创建失败：{ex.Message}";
                return false;
            }
        }

        public bool TrySetUserEnabled(
            AuthenticatedUser administrator,
            long targetUserId,
            bool isEnabled,
            out string? errorMessage)
        {
            if (targetUserId <= 0)
            {
                errorMessage = "请选择需要操作的用户。";
                return false;
            }

            if (!isEnabled && targetUserId == administrator.Id)
            {
                errorMessage = "当前管理员不能禁用自己的账户。";
                return false;
            }

            return TryExecuteAdministratorUpdate(
                administrator,
                @"
                UPDATE app_users
                SET is_enabled = $value
                WHERE id = $targetUserId;
                ",
                targetUserId,
                isEnabled ? 1 : 0,
                "账户状态更新",
                out errorMessage);
        }

        public bool TryResetPassword(
            AuthenticatedUser administrator,
            long targetUserId,
            string temporaryPassword,
            out string? errorMessage)
        {
            if (targetUserId <= 0)
            {
                errorMessage = "请选择需要重置密码的用户。";
                return false;
            }

            if (!TryValidateNewPassword(temporaryPassword, out errorMessage))
            {
                return false;
            }

            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    if (!TryVerifyAdministrator(
                            connection,
                            administrator,
                            out errorMessage))
                    {
                        return false;
                    }

                    byte[] salt =
                        RandomNumberGenerator.GetBytes(SaltSizeBytes);
                    byte[] hash = HashPassword(
                        temporaryPassword,
                        salt,
                        PasswordIterationCount);
                    try
                    {
                        using SqliteCommand command = connection.CreateCommand();
                        command.CommandText =
                            @"
                            UPDATE app_users
                            SET password_salt = $salt,
                                password_hash = $hash,
                                password_iterations = $iterations,
                                must_change_password = 1
                            WHERE id = $targetUserId;
                            ";
                        command.Parameters.Add("$salt", SqliteType.Blob).Value = salt;
                        command.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
                        command.Parameters.AddWithValue(
                            "$iterations",
                            PasswordIterationCount);
                        command.Parameters.AddWithValue(
                            "$targetUserId",
                            targetUserId);
                        if (command.ExecuteNonQuery() != 1)
                        {
                            errorMessage = "没有找到需要重置密码的用户。";
                            return false;
                        }
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(hash);
                    }
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"密码重置失败：{ex.Message}";
                return false;
            }
        }

        private bool TryExecuteAdministratorUpdate(
            AuthenticatedUser administrator,
            string commandText,
            long targetUserId,
            int value,
            string operationName,
            out string? errorMessage)
        {
            try
            {
                lock (databaseSync)
                {
                    using SqliteConnection connection = OpenConnection();
                    if (!TryVerifyAdministrator(
                            connection,
                            administrator,
                            out errorMessage))
                    {
                        return false;
                    }

                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = commandText;
                    command.Parameters.AddWithValue("$value", value);
                    command.Parameters.AddWithValue(
                        "$targetUserId",
                        targetUserId);
                    if (command.ExecuteNonQuery() != 1)
                    {
                        errorMessage = "没有找到目标用户。";
                        return false;
                    }
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"{operationName}失败：{ex.Message}";
                return false;
            }
        }

        private static bool TryVerifyAdministrator(
            SqliteConnection connection,
            AuthenticatedUser administrator,
            out string? errorMessage)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                @"
                SELECT role, is_enabled
                FROM app_users
                WHERE id = $id;
                ";
            command.Parameters.AddWithValue("$id", administrator.Id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(1) != 1 ||
                !string.Equals(
                    reader.GetString(0),
                    UserRole.Administrator.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "当前账户没有用户管理权限。";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static bool TryValidateUsername(
            string username,
            out string? errorMessage)
        {
            string normalized = (username ?? string.Empty).Trim();
            if (normalized.Length < 3 || normalized.Length > 50)
            {
                errorMessage = "用户名长度必须为3～50个字符。";
                return false;
            }

            foreach (char character in normalized)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '_' && character != '-' && character != '.')
                {
                    errorMessage = "用户名只能包含字母、数字、下划线、短横线和点。";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        private static bool TryValidateDisplayName(
            string displayName,
            out string? errorMessage)
        {
            string normalized = (displayName ?? string.Empty).Trim();
            if (normalized.Length < 1 || normalized.Length > 50)
            {
                errorMessage = "显示名称长度必须为1～50个字符。";
                return false;
            }

            foreach (char character in normalized)
            {
                if (char.IsControl(character))
                {
                    errorMessage = "显示名称不能包含换行等控制字符。";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        private static bool TryValidateNewPassword(
            string password,
            out string? errorMessage)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 10)
            {
                errorMessage = "新密码至少需要 10 个字符。";
                return false;
            }

            if (password.Length > 128)
            {
                errorMessage = "新密码最多允许 128 个字符。";
                return false;
            }

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;
            bool hasSpecial = false;
            foreach (char character in password)
            {
                if (char.IsUpper(character))
                {
                    hasUpper = true;
                }
                else if (char.IsLower(character))
                {
                    hasLower = true;
                }
                else if (char.IsDigit(character))
                {
                    hasDigit = true;
                }
                else
                {
                    hasSpecial = true;
                }
            }

            if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
            {
                errorMessage =
                    "新密码必须同时包含大写字母、小写字母、数字和特殊字符。";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static void InsertUser(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string username,
            string displayName,
            UserRole role,
            string initialPassword)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = HashPassword(
                initialPassword,
                salt,
                PasswordIterationCount);

            try
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    @"
                    INSERT INTO app_users
                    (
                        normalized_username,
                        username,
                        display_name,
                        role,
                        password_salt,
                        password_hash,
                        password_iterations,
                        is_enabled,
                        must_change_password,
                        created_at_utc
                    )
                    VALUES
                    (
                        $normalizedUsername,
                        $username,
                        $displayName,
                        $role,
                        $passwordSalt,
                        $passwordHash,
                        $passwordIterations,
                        1,
                        1,
                        $createdAtUtc
                    );
                    ";
                command.Parameters.AddWithValue(
                    "$normalizedUsername",
                    NormalizeUsername(username));
                command.Parameters.AddWithValue("$username", username);
                command.Parameters.AddWithValue("$displayName", displayName);
                command.Parameters.AddWithValue("$role", role.ToString());
                command.Parameters.Add("$passwordSalt", SqliteType.Blob).Value = salt;
                command.Parameters.Add("$passwordHash", SqliteType.Blob).Value = hash;
                command.Parameters.AddWithValue(
                    "$passwordIterations",
                    PasswordIterationCount);
                command.Parameters.AddWithValue(
                    "$createdAtUtc",
                    DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }

        private static byte[] HashPassword(
            string password,
            byte[] salt,
            int iterationCount)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterationCount,
                HashAlgorithmName.SHA256,
                HashSizeBytes);
        }

        private static string NormalizeUsername(string? username)
        {
            return (username ?? string.Empty).Trim().ToUpperInvariant();
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
