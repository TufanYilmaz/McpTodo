-- Supabase PostgreSQL Schema for MCP Todo System
-- Phase 1: 01-supabase-efcore (DATA-01, DATA-02, DATA-04, D-04, D-05, D-06, D-07, D-09)

-- 1. Users Table (text PK to support Descope auth sub identifier)
CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    name TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 2. Projects Table (bigint identity PK, soft delete enabled)
CREATE TABLE IF NOT EXISTS projects (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 3. Todos Table (bigint identity PK, cascade delete with projects, soft delete enabled)
CREATE TABLE IF NOT EXISTS todos (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id BIGINT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    due_date TIMESTAMP WITH TIME ZONE,
    is_completed BOOLEAN NOT NULL DEFAULT FALSE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 4. Indexes for query optimization (filtering by user_id and soft-delete status)
CREATE INDEX IF NOT EXISTS idx_projects_user_deleted ON projects(user_id, is_deleted);
CREATE INDEX IF NOT EXISTS idx_todos_user_deleted ON todos(user_id, is_deleted);
CREATE INDEX IF NOT EXISTS idx_todos_project_id ON todos(project_id);
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);

-- 5. Default Development User Seed (per D-01)
INSERT INTO users (id, email, name)
VALUES ('default-user', 'dev@example.com', 'Default Dev User')
ON CONFLICT (id) DO NOTHING;
