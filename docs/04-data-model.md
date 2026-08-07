# 04 — Модели маълумот

> Ҷадвалҳо аз рӯи фаза гурӯҳбандӣ шудаанд. Ҷадвали фазаи оянда пеш аз вақт насоз.

## Фазаи 1 — Auth

```
users(id, full_name, username UNIQUE, password_hash, avatar_url, avatar_path, phone,
      email, birth_date, address, gender, contract_document_path, contract_document_file_name,
      is_active, must_change_password, only_assigned,
      permissions_version, created_at, last_login_at)

roles(id, key UNIQUE, name, description, is_system)

role_permissions(role_id, permission_key)          PK(role_id, permission_key)
user_roles(user_id, role_id)                       PK(user_id, role_id)
user_permissions(user_id, permission_key, is_granted)  PK(user_id, permission_key)

refresh_tokens(id, user_id, token_hash, expires_at, revoked_at, created_by_ip)
```

## Фазаи 2 — Проект ва таск

```
projects(id, name, key, color, is_archived, created_at)
project_members(project_id, user_id)

board_columns(id, project_id, name, order_index, is_done_column)

tasks(id, project_id, column_id, title, description, assignee_id,
      priority, due_date, position, created_by, created_at, updated_at)

task_comments(id, task_id, author_id, body, created_at)
task_attachments(id, task_id, file_name, file_path, size_bytes, uploaded_by)
labels(id, project_id, name, color)
task_labels(task_id, label_id)
task_activity(id, task_id, user_id, action, payload_json, created_at)
```

**`position`:** `double precision`. Ҳангоми партофтан дар байни ду таск — миёнаи `position`-и онҳо. Қадами аввалия 1000. Агар фарқ аз `0.0001` хурд шавад — колоннаро reindex кун.

## Фазаи 4–6 — Каналҳо ва инбокс

```
channels(id, type, name, external_id, credentials_encrypted,
         is_active, created_at)
         -- type: whatsapp | instagram | facebook

channel_members(channel_id, user_id)

conversations(id, channel_id, external_id, contact_name, contact_avatar_url,
              status, assigned_to, last_message_at, unread_count,
              window_expires_at, created_at)
              -- status: new | in_progress | waiting | closed
              UNIQUE(channel_id, external_id)

messages(id, conversation_id, direction, type, body, media_url,
         external_id UNIQUE, delivery_status, is_internal_note,
         sent_by_user_id, created_at)
         -- direction: inbound | outbound
         -- type: text | image | video | audio | file | story_reply
         -- delivery_status: pending | sent | delivered | read | failed

conversation_tags(conversation_id, tag_id)
tags(id, name, color)

message_templates(id, title, shortcut, body, channel_type, created_by)

webhook_logs(id, provider, raw_json, received_at, processed_at, error)
notifications(id, user_id, type, payload_json, is_read, created_at)
```

## Index-ҳои ҳатмӣ

```sql
CREATE INDEX ON tasks (project_id, column_id, position);
CREATE INDEX ON tasks (assignee_id) WHERE assignee_id IS NOT NULL;
CREATE INDEX ON conversations (channel_id, status, last_message_at DESC);
CREATE INDEX ON conversations (assigned_to) WHERE assigned_to IS NOT NULL;
CREATE INDEX ON messages (conversation_id, created_at DESC);
CREATE UNIQUE INDEX ON messages (external_id) WHERE external_id IS NOT NULL;
```

## Қоидаҳо

- Ҳама сана `timestamptz`, UTC
- ID — `uuid` (`Guid.CreateVersion7()`)
- Нест кардани корманд — soft: `is_active = false`. Ҷисмонӣ нест накун, таск ва чат мемонанд
- `credentials_encrypted` — Data Protection API, ҳеҷ гоҳ матни оддӣ
