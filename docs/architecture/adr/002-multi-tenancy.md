# ADR-002 — Multi-Tenancy desde o primeiro dia

- **Status:** Accepted
- **Data:** 2026-09-10

## Contexto

A plataforma atenderá múltiplos estabelecimentos independentes. Cada estabelecimento possui serviços, profissionais, clientes, appointments, pagamentos, conversas, configurações e métricas próprias.

Adicionar isolamento de tenant somente depois do MVP criaria alto risco de segurança e grande custo de refatoração.

## Decisão

A aplicação será **Multi-Tenant desde o primeiro dia**.

No MVP utilizaremos PostgreSQL compartilhado e isolamento lógico por `TenantId` para os dados pertencentes aos estabelecimentos.

O contexto de tenant deve ser estabelecido na entrada da requisição/processamento e propagado por toda operação relevante.

## Regras

- entidades pertencentes ao estabelecimento carregam `TenantId`;
- autorização deve validar acesso ao tenant;
- consultas devem aplicar isolamento de tenant de forma sistemática;
- jobs, webhooks e eventos também devem possuir contexto de tenant;
- chaves e constraints devem considerar tenant quando necessário;
- testes automatizados devem validar ausência de vazamento entre tenants;
- logs devem permitir correlação por tenant sem expor dados pessoais desnecessários.

## Estratégia de implementação

### Tenant como boundary de isolamento

`Tenant` representa a raiz de isolamento, segurança e billing da plataforma.

A própria entidade `Tenant` não implementa `ITenantScoped`, pois ela representa a raiz do isolamento e não um recurso pertencente a outro tenant.

Entidades que pertencem a um tenant devem implementar `ITenantScoped` e possuir `TenantId`.

### TenantContext

O tenant corrente é disponibilizado pela abstração `ITenantContext`.

A implementação `TenantContext` possui ciclo de vida `Scoped`, garantindo um contexto independente por operação/requisição.

A inicialização é feita por meio de `ITenantContextInitializer` e pode ocorrer apenas uma vez durante o ciclo de vida do contexto.

O `TenantId` nunca deve ser considerado confiável quando fornecido diretamente pelo cliente da API.

A resolução do tenant a partir de uma identidade autenticada será responsabilidade da camada de Identity/Auth e deverá inicializar o `TenantContext` na entrada da requisição.

### Isolamento de leitura

Entidades `ITenantScoped` persistidas pelo Entity Framework Core devem possuir Global Query Filter baseado no tenant corrente.

O modelo possui teste arquitetural automatizado que verifica se todas as entidades `ITenantScoped` mapeadas pelo EF Core possuem Query Filter.

Isso reduz o risco de uma nova entidade tenant-scoped ser adicionada ao modelo sem isolamento de leitura.

O uso de `IgnoreQueryFilters()` deve ficar restrito a cenários controlados de infraestrutura, administração, migração ou testes e não deve fazer parte do fluxo normal de negócio.

### Isolamento de escrita

Antes da persistência, o `AppDbContext` valida entidades `ITenantScoped` nos estados:

- `Added`;
- `Modified`;
- `Deleted`.

Uma alteração cujo `TenantId` seja diferente do tenant corrente é rejeitada.

Operações envolvendo somente entidades raiz ou globais, como a criação de um `Tenant`, não exigem artificialmente um `TenantContext` inicializado.

### Defesa em profundidade

O isolamento multi-tenant não depende de uma única barreira.

A estratégia utiliza:

1. resolução confiável do tenant na entrada da operação;
2. `TenantContext` scoped;
3. Global Query Filters para leitura;
4. validação de ownership antes da escrita;
5. constraints e índices PostgreSQL quando necessários;
6. testes automatizados de isolamento.

Constraints compostas envolvendo `TenantId` devem ser adicionadas quando trouxerem proteção real para relacionamentos críticos, evitando a criação indiscriminada de índices e foreign keys compostas.

### Testes de isolamento

Os testes de integração utilizam PostgreSQL real e validam cenários com tenants distintos.

A cobertura atual inclui:

- isolamento de listagem entre Tenant A e Tenant B;
- consulta direta por identificador pertencente a outro tenant;
- bloqueio de inserção cross-tenant;
- bloqueio de atualização cross-tenant;
- bloqueio de exclusão cross-tenant;
- persistência de entidades raiz sem contexto artificial de tenant.

Além dos testes de integração, existe um teste arquitetural que impede que uma entidade `ITenantScoped` mapeada pelo EF Core seja adicionada sem Query Filter.

## Limites da implementação atual

A fundação de isolamento multi-tenant não implementa autenticação ou autorização.

A resolução confiável do `TenantId` a partir do usuário autenticado será implementada na etapa de Identity/Auth.

Essa etapa deverá estabelecer a associação entre identidade e tenant e inicializar o `TenantContext` sem aceitar o `TenantId` enviado pelo cliente como autoridade.

O comportamento HTTP para acesso cross-tenant, incluindo retorno `404` para recursos não visíveis ao tenant corrente, será implementado e validado na camada de API quando os endpoints autenticados forem introduzidos.

Jobs, webhooks e consumidores assíncronos também deverão estabelecer explicitamente um contexto de tenant confiável antes de executar operações tenant-scoped.

## Consequências positivas

- segurança estrutural desde o início;
- modelo SaaS suportado nativamente;
- métricas e custos podem ser calculados por tenant;
- facilita Billing, Usage Metering e auditoria.

## Consequências negativas

- exige disciplina em todas as consultas e comandos;
- aumenta importância de testes de autorização/isolamento;
- índices e constraints precisam considerar o modelo Multi-Tenant.

## Não decidido agora

Não adotaremos no MVP:

- banco separado por tenant;
- schema PostgreSQL separado por tenant;
- infraestrutura dedicada por cliente.

Essas opções podem ser avaliadas futuramente para requisitos específicos de escala, contrato ou isolamento.
