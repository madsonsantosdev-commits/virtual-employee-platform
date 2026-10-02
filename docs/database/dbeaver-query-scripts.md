# DBeaver — PostgreSQL Query Scripts

Consultas SQL utilizadas para inspeção, diagnóstico e validação manual do banco PostgreSQL do **Virtual Employee** durante o desenvolvimento.

Este documento acompanha a evolução do modelo de dados e deve ser atualizado conforme novos módulos forem implementados.

> **Importante:** estes scripts são destinados principalmente à leitura e diagnóstico.
> Não armazenar passwords, connection strings, tokens ou outros secrets neste arquivo.

---

## 1. Objetivo

As consultas deste documento servem para:

- inspecionar os dados persistidos no PostgreSQL;
- validar manualmente operações realizadas pela API;
- facilitar testes durante o desenvolvimento;
- investigar relacionamentos entre entidades;
- verificar dados por tenant;
- apoiar diagnósticos pelo DBeaver;
- complementar, e não substituir, os testes automatizados.

Sempre que possível, alterações de estado devem ser realizadas pela API da aplicação.

O DBeaver deve ser utilizado principalmente para **consulta e diagnóstico**, evitando alterações manuais que possam mascarar problemas na aplicação.

---

## 2. Ambiente local de desenvolvimento

Os exemplos atuais utilizam o seguinte tenant:

```text
10000000-0000-4000-8000-000000000001
```

Business utilizado nos exemplos:

```text
11000000-0000-4000-8000-000000000001
```

Location utilizada nos exemplos:

```text
62c8f122-8a8f-4d32-9c35-25ab6ab2571a
```

Esses identificadores pertencem ao ambiente local de desenvolvimento e podem mudar caso o banco seja recriado ou os dados sejam removidos.

---

# 3. Tenants

## 3.1 Listar todos os tenants

```sql
SELECT
    id,
    name
FROM tenants
ORDER BY name;
```

## 3.2 Consultar o tenant utilizado no desenvolvimento

```sql
SELECT
    id,
    name
FROM tenants
WHERE id = '10000000-0000-4000-8000-000000000001';
```

---

# 4. Business Types

`business_types` é um catálogo global e não pertence a um tenant específico.

## 4.1 Listar todos os tipos de negócio

```sql
SELECT
    id,
    code,
    name,
    is_system,
    is_active,
    created_at,
    updated_at
FROM business_types
ORDER BY name;
```

## 4.2 Listar apenas tipos de negócio ativos

```sql
SELECT
    id,
    code,
    name
FROM business_types
WHERE is_active = TRUE
ORDER BY name;
```

## 4.3 Localizar Business Type pelo código

Exemplo utilizando `BARBERSHOP`:

```sql
SELECT
    id,
    code,
    name,
    is_system,
    is_active
FROM business_types
WHERE code = 'BARBERSHOP';
```

---

# 5. Businesses

## 5.1 Listar Businesses de um tenant

```sql
SELECT
    id,
    tenant_id,
    business_type_id,
    name,
    slot_interval_minutes,
    is_active,
    created_at,
    updated_at
FROM businesses
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY name;
```

## 5.2 Consultar o Business utilizado no desenvolvimento

```sql
SELECT
    id,
    tenant_id,
    business_type_id,
    name,
    slot_interval_minutes,
    is_active,
    created_at,
    updated_at
FROM businesses
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
  AND id = '11000000-0000-4000-8000-000000000001';
```

## 5.3 Business com seu Business Type

```sql
SELECT
    b.id AS business_id,
    b.name AS business_name,
    bt.id AS business_type_id,
    bt.code AS business_type_code,
    bt.name AS business_type_name,
    b.slot_interval_minutes,
    b.is_active
FROM businesses b
INNER JOIN business_types bt
    ON bt.id = b.business_type_id
WHERE b.tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY b.name;
```

---

# 6. Locations

## 6.1 Listar Locations de um tenant

```sql
SELECT
    id,
    tenant_id,
    business_id,
    name,
    phone,
    address,
    country_code,
    timezone,
    is_active,
    created_at,
    updated_at
FROM locations
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY name;
```

## 6.2 Consultar uma Location específica

```sql
SELECT
    id,
    tenant_id,
    business_id,
    name,
    phone,
    address,
    country_code,
    timezone,
    is_active,
    created_at,
    updated_at
FROM locations
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
  AND id = '62c8f122-8a8f-4d32-9c35-25ab6ab2571a';
```

## 6.3 Locations com seus Businesses

```sql
SELECT
    l.id AS location_id,
    l.name AS location_name,
    b.id AS business_id,
    b.name AS business_name,
    l.phone,
    l.address,
    l.country_code,
    l.timezone,
    l.is_active
FROM locations l
INNER JOIN businesses b
    ON b.tenant_id = l.tenant_id
   AND b.id = l.business_id
WHERE l.tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY l.name;
```

## 6.4 Quantidade de Locations por Business

```sql
SELECT
    b.id AS business_id,
    b.name AS business_name,
    COUNT(l.id) AS location_count
FROM businesses b
LEFT JOIN locations l
    ON l.tenant_id = b.tenant_id
   AND l.business_id = b.id
WHERE b.tenant_id = '10000000-0000-4000-8000-000000000001'
GROUP BY
    b.id,
    b.name
ORDER BY b.name;
```

---

# 7. Services

## 7.1 Listar Services de um tenant

```sql
SELECT
    id,
    tenant_id,
    business_id,
    name,
    service_type,
    price,
    duration_minutes,
    is_active,
    created_at,
    updated_at
FROM services
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY name;
```

## 7.2 Listar Services de um Business

```sql
SELECT
    id,
    name,
    service_type,
    price,
    duration_minutes,
    is_active,
    created_at,
    updated_at
FROM services
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
  AND business_id = '11000000-0000-4000-8000-000000000001'
ORDER BY name;
```

## 7.3 Listar apenas Services ativos

```sql
SELECT
    id,
    business_id,
    name,
    service_type,
    price,
    duration_minutes
FROM services
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
  AND is_active = TRUE
ORDER BY name;
```

## 7.4 Listar Services por tipo

```sql
SELECT
    id,
    name,
    service_type,
    price,
    duration_minutes,
    is_active
FROM services
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY
    service_type,
    name;
```

## 7.5 Services atualmente utilizados nos testes manuais

```sql
SELECT
    id,
    name,
    service_type,
    price,
    duration_minutes,
    is_active
FROM services
WHERE tenant_id = '10000000-0000-4000-8000-000000000001'
  AND id IN (
      '325c9cc2-5b2a-46e2-9049-b5b1b50e9306',
      '8bb46f11-dd3d-44d2-b2c1-ae8201f61276',
      '21924a9e-1b05-4a30-b3d8-557f14a3eb70'
  )
ORDER BY name;
```

Os dados locais utilizados durante a validação atual incluem:

| Service | Tipo |
| --- | --- |
| Barba | SINGLE |
| Corte masculino premium | SINGLE |
| Corte + Barba | COMBO |

Os GUIDs são dados locais de desenvolvimento e não devem ser tratados como identificadores fixos de domínio.

---

# 8. Service Components / Combos

Um Service do tipo `COMBO` é composto por Services do tipo `SINGLE`.

A composição é armazenada em `service_components`.

## 8.1 Listar todos os componentes dos combos

```sql
SELECT
    sc.tenant_id,
    combo.id AS combo_service_id,
    combo.name AS combo_name,
    component.id AS component_service_id,
    component.name AS component_name,
    sc.sort_order,
    sc.created_at
FROM service_components sc
INNER JOIN services combo
    ON combo.tenant_id = sc.tenant_id
   AND combo.id = sc.combo_service_id
INNER JOIN services component
    ON component.tenant_id = sc.tenant_id
   AND component.id = sc.component_service_id
WHERE sc.tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY
    combo.name,
    sc.sort_order;
```

## 8.2 Consultar componentes de um Combo específico

Exemplo com o serviço local `Corte + Barba`:

```sql
SELECT
    combo.id AS combo_service_id,
    combo.name AS combo_name,
    component.id AS component_service_id,
    component.name AS component_name,
    component.price,
    component.duration_minutes,
    sc.sort_order
FROM service_components sc
INNER JOIN services combo
    ON combo.tenant_id = sc.tenant_id
   AND combo.id = sc.combo_service_id
INNER JOIN services component
    ON component.tenant_id = sc.tenant_id
   AND component.id = sc.component_service_id
WHERE sc.tenant_id = '10000000-0000-4000-8000-000000000001'
  AND sc.combo_service_id = '8bb46f11-dd3d-44d2-b2c1-ae8201f61276'
ORDER BY sc.sort_order;
```

---

# 9. Location Services

`location_services` representa quais Services são oferecidos por uma determinada Location.

A relação não possui um `Id` artificial.

A chave é composta por:

```text
TenantId + LocationId + ServiceId
```

A existência do vínculo significa que o Service está disponível naquela Location.

---

## 9.1 Listar vínculos brutos de uma Location

```sql
SELECT
    ls.tenant_id,
    ls.location_id,
    ls.service_id,
    ls.created_at
FROM location_services ls
WHERE ls.tenant_id = '10000000-0000-4000-8000-000000000001'
  AND ls.location_id = '62c8f122-8a8f-4d32-9c35-25ab6ab2571a'
ORDER BY ls.created_at;
```

Essa consulta é útil para verificar diretamente a tabela associativa.

---

## 9.2 Serviços oferecidos por uma Location

Consulta recomendada para validar o resultado de:

```text
PUT /api/v1/locations/{id}/services
```

```sql
SELECT
    l.id AS location_id,
    l.name AS location_name,
    s.id AS service_id,
    s.name AS service_name,
    s.service_type,
    s.price,
    s.duration_minutes,
    s.is_active,
    ls.created_at
FROM location_services ls
INNER JOIN locations l
    ON l.tenant_id = ls.tenant_id
   AND l.id = ls.location_id
INNER JOIN services s
    ON s.tenant_id = ls.tenant_id
   AND s.id = ls.service_id
WHERE ls.tenant_id = '10000000-0000-4000-8000-000000000001'
  AND ls.location_id = '62c8f122-8a8f-4d32-9c35-25ab6ab2571a'
ORDER BY s.name;
```

Durante a validação manual da feature `LocationService`, foram vinculados:

```text
Barba
Corte masculino premium
```

à Location:

```text
Unidade Vila Mariana Atualizada
```

A operação foi realizada através do endpoint:

```text
PUT /api/v1/locations/{id}/services
```

e validada posteriormente diretamente no PostgreSQL.

---

## 9.3 Todas as Locations e seus Services

```sql
SELECT
    b.id AS business_id,
    b.name AS business_name,
    l.id AS location_id,
    l.name AS location_name,
    s.id AS service_id,
    s.name AS service_name,
    s.service_type,
    s.price,
    s.duration_minutes,
    s.is_active
FROM location_services ls
INNER JOIN locations l
    ON l.tenant_id = ls.tenant_id
   AND l.id = ls.location_id
INNER JOIN businesses b
    ON b.tenant_id = l.tenant_id
   AND b.id = l.business_id
INNER JOIN services s
    ON s.tenant_id = ls.tenant_id
   AND s.id = ls.service_id
WHERE ls.tenant_id = '10000000-0000-4000-8000-000000000001'
ORDER BY
    b.name,
    l.name,
    s.name;
```

---

## 9.4 Quantidade de Services por Location

```sql
SELECT
    l.id AS location_id,
    l.name AS location_name,
    COUNT(ls.service_id) AS service_count
FROM locations l
LEFT JOIN location_services ls
    ON ls.tenant_id = l.tenant_id
   AND ls.location_id = l.id
WHERE l.tenant_id = '10000000-0000-4000-8000-000000000001'
GROUP BY
    l.id,
    l.name
ORDER BY l.name;
```

O `LEFT JOIN` permite visualizar também Locations que ainda não possuem Services associados.

---

## 9.5 Locations que oferecem determinado Service

Substituir o `service_id` conforme necessário.

```sql
SELECT
    s.id AS service_id,
    s.name AS service_name,
    l.id AS location_id,
    l.name AS location_name,
    l.is_active AS location_is_active
FROM location_services ls
INNER JOIN services s
    ON s.tenant_id = ls.tenant_id
   AND s.id = ls.service_id
INNER JOIN locations l
    ON l.tenant_id = ls.tenant_id
   AND l.id = ls.location_id
WHERE ls.tenant_id = '10000000-0000-4000-8000-000000000001'
  AND ls.service_id = '325c9cc2-5b2a-46e2-9049-b5b1b50e9306'
ORDER BY l.name;
```

---

# 10. Tenant Isolation Diagnostics

As consultas desta seção permitem inspecionar a distribuição física dos registros entre tenants.

Elas são ferramentas de diagnóstico e **não substituem os testes automatizados de Tenant Isolation** existentes no projeto.

## 10.1 Quantidade de Businesses por tenant

```sql
SELECT
    tenant_id,
    COUNT(*) AS business_count
FROM businesses
GROUP BY tenant_id
ORDER BY tenant_id;
```

## 10.2 Quantidade de Locations por tenant

```sql
SELECT
    tenant_id,
    COUNT(*) AS location_count
FROM locations
GROUP BY tenant_id
ORDER BY tenant_id;
```

## 10.3 Quantidade de Services por tenant

```sql
SELECT
    tenant_id,
    COUNT(*) AS service_count
FROM services
GROUP BY tenant_id
ORDER BY tenant_id;
```

## 10.4 Quantidade de Service Components por tenant

```sql
SELECT
    tenant_id,
    COUNT(*) AS service_component_count
FROM service_components
GROUP BY tenant_id
ORDER BY tenant_id;
```

## 10.5 Quantidade de Location Services por tenant

```sql
SELECT
    tenant_id,
    COUNT(*) AS location_service_count
FROM location_services
GROUP BY tenant_id
ORDER BY tenant_id;
```

---

# 11. Visão geral do tenant

Consulta útil para obter uma visão rápida dos principais registros atualmente implementados.

```sql
SELECT
    t.id AS tenant_id,
    t.name AS tenant_name,
    (
        SELECT COUNT(*)
        FROM businesses b
        WHERE b.tenant_id = t.id
    ) AS businesses,
    (
        SELECT COUNT(*)
        FROM locations l
        WHERE l.tenant_id = t.id
    ) AS locations,
    (
        SELECT COUNT(*)
        FROM services s
        WHERE s.tenant_id = t.id
    ) AS services,
    (
        SELECT COUNT(*)
        FROM location_services ls
        WHERE ls.tenant_id = t.id
    ) AS location_services
FROM tenants t
WHERE t.id = '10000000-0000-4000-8000-000000000001';
```

---

# 12. Verificação de coerência Location → Service

Esta consulta ajuda a detectar manualmente vínculos em que Location e Service não pertencem ao mesmo Business.

Em condições normais, a consulta deve retornar **zero registros**.

```sql
SELECT
    ls.tenant_id,
    l.id AS location_id,
    l.name AS location_name,
    l.business_id AS location_business_id,
    s.id AS service_id,
    s.name AS service_name,
    s.business_id AS service_business_id
FROM location_services ls
INNER JOIN locations l
    ON l.tenant_id = ls.tenant_id
   AND l.id = ls.location_id
INNER JOIN services s
    ON s.tenant_id = ls.tenant_id
   AND s.id = ls.service_id
WHERE l.business_id <> s.business_id
ORDER BY
    ls.tenant_id,
    l.name,
    s.name;
```

> A regra de mesmo Business é validada pela aplicação.
> Esta consulta existe apenas como ferramenta adicional de diagnóstico.

---

# 13. Verificação de duplicidade de Location Services

A chave composta da tabela impede vínculos duplicados.

A consulta abaixo pode ser utilizada para inspeção adicional.

Em condições normais, deve retornar **zero registros**.

```sql
SELECT
    tenant_id,
    location_id,
    service_id,
    COUNT(*) AS duplicate_count
FROM location_services
GROUP BY
    tenant_id,
    location_id,
    service_id
HAVING COUNT(*) > 1;
```

---

# 14. Fluxo de validação recomendado

Ao validar funcionalidades manualmente:

1. Execute a operação através da API.
2. Confirme o status HTTP e o payload retornado.
3. Consulte o estado através do endpoint GET correspondente.
4. Quando necessário, utilize as consultas deste documento no DBeaver.
5. Não altere manualmente o banco para fazer um teste da aplicação passar.
6. Utilize os testes automatizados como principal mecanismo de regressão.

Exemplo para `LocationService`:

```text
PUT /api/v1/locations/{id}/services
              |
              v
ReplaceLocationServicesHandler
              |
              v
LocationServiceWriteService
              |
              v
EF Core / PostgreSQL
              |
              v
location_services
```

A leitura segue:

```text
GET /api/v1/locations/{id}/services
              |
              v
GetLocationServicesHandler
              |
              v
LocationServiceReadService
              |
              v
EF Core / PostgreSQL
              |
              v
ServiceResponse[]
```

---

# 15. Segurança e boas práticas

- Nunca versionar passwords ou connection strings reais.
- Nunca armazenar tokens ou secrets neste arquivo.
- Sempre filtrar dados tenant-scoped pelo `tenant_id` durante inspeções.
- Evitar `UPDATE`, `DELETE`, `TRUNCATE` e alterações manuais durante testes funcionais.
- Preferir alterações realizadas pela API.
- Não utilizar dados locais como contratos permanentes da aplicação.
- Não utilizar DBeaver como substituto das regras de domínio.
- Não utilizar consultas manuais como substituto dos testes automatizados.
- Queries de diagnóstico podem ignorar deliberadamente os filtros do EF Core; portanto, devem ser utilizadas com atenção.

---

# 16. Evolução deste documento

Novas seções devem ser adicionadas conforme os próximos componentes forem implementados.

Sequência prevista de evolução do modelo:

```text
Tenant
  └── Business
       ├── Location
       │    └── LocationService
       │
       └── Service
            └── ServiceComponent

Próximas etapas:
  Professional
  ProfessionalLocation
  ProfessionalService
  AvailabilityRule
  ScheduleBlock
  Client
  Appointment
  Payment
  Refund
```

Não adicionar consultas de tabelas que ainda não existem no modelo implementado.

A documentação deve acompanhar o estado real do código e das migrations.