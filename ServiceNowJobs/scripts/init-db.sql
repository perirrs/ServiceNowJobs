-- SNHub Dev Database Initialisation
-- Runs automatically when PostgreSQL container first starts.
-- Creates all service databases. snhub_auth_dev is the POSTGRES_DB (already exists).

CREATE DATABASE snhub_users_dev;
CREATE DATABASE snhub_jobs_dev;
CREATE DATABASE snhub_applications_dev;
CREATE DATABASE snhub_profiles_dev;
CREATE DATABASE snhub_notifications_dev;

-- Enable pg_trgm (trigram search) in each database
\c snhub_auth_dev
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c snhub_users_dev
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c snhub_jobs_dev
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c snhub_applications_dev
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c snhub_profiles_dev
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c snhub_notifications_dev
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
