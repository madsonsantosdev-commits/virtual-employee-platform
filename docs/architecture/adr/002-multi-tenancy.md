# ADR-002 — Multi-Tenancy desde o primeiro dia

- **Status:** Accepted
- **Data:** 2026-09-10
- **Última atualização:** 2026-09-22

## Contexto

A plataforma atenderá múltiplos estabelecimentos independentes. Cada estabelecimento possui serviços, profissionais, clientes, appointments, pagamentos, conversas, configurações e métricas próprias.

Adicionar isolamento de tenant somente depois do MVP criaria alto risco de segurança e grande custo de refatoração.

Além do isolamento de leitura, a implementação precisa proteger operações de escrita realizadas com entidades desconectadas do Entity Framework Core.

Uma validação baseada apenas no `TenantId` presente na entidade em memória não é suficiente. Um objeto desconectado pode informar o `TenantId` do tenant corrente enquanto reutiliza o identificador (`Id`) de um registro pertencente a outro tenant.

Sem uma proteção adicional, um `UPDATE` ou `DELETE` baseado somente na chave primária poderia atingir fisicamente o registro de outro tenant.

## Decisão

A aplicação será **Multi-Tenant desde o primeiro dia**.

No MVP utilizaremos PostgreSQL compartilhado e isolamento lógico por `TenantId` para os dados pertencentes aos estabelecimentos.

O contexto de tenant deve ser estabelecido na entrada da requisição ou processamento e propagado por toda operação relevante.

O isolamento tenant-scoped utilizará defesa em profundidade, combinando:

- `TenantContext` scoped;
- Global Query Filters para leitura;
- validação de ownership antes da escrita;
- `TenantId` como concurrency token para entidades `ITenantScoped`;
- constraints e índices PostgreSQL quando necessários;
- testes automatizados com PostgreSQL real.

## Regras

- entidades pertencentes ao estabelecimento carregam `TenantId`;
- entidades `ITenantScoped` persistidas pelo EF Core devem possuir Query Filter;
- `TenantId` de entidades `ITenantScoped` deve ser configurado como concurrency token;
- autorização deve validar acesso ao tenant;
- consultas devem aplicar isolamento de tenant de forma sistemática;
- alterações devem validar ownership antes da persistência;
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

A implementação `TenantContext` possui ciclo de vida `Scoped`, garantindo um contexto independente por operação ou requisição.

A inicialização é feita por meio de `ITenantContextInitializer` e pode ocorrer apenas uma vez durante o ciclo de vida do contexto.

O `TenantId` nunca deve ser considerado confiável quando fornecido diretamente pelo cliente da API.

A resolução do tenant a partir de uma identidade autenticada será responsabilidade da camada de Identity/Auth e deverá inicializar o `TenantContext` na entrada da requisição.

### Isolamento de leitura

Entidades `ITenantScoped` persistidas pelo Entity Framework Core devem possuir Global Query Filter baseado no tenant corrente.

O modelo possui teste arquitetural automatizado que verifica se todas as entidades `ITenantScoped` mapeadas pelo EF Core possuem Query Filter.

Isso reduz o risco de uma nova entidade tenant-scoped ser adicionada ao modelo sem uma barreira estrutural de isolamento de leitura.

O comportamento efetivo do isolamento é validado por testes de integração contra PostgreSQL real.

O uso de `IgnoreQueryFilters()` deve ficar restrito a cenários controlados de infraestrutura, administração, migração ou testes e não deve fazer parte do fluxo normal de negócio.

### Isolamento de escrita

Antes da persistência, o `AppDbContext` valida entidades `ITenantScoped` nos estados:

- `Added`;
- `Modified`;
- `Deleted`.

Uma alteração cujo `TenantId` seja diferente do tenant corrente é rejeitada antes de chegar ao banco.

Operações envolvendo somente entidades raiz ou globais, como a criação de um `Tenant`, não exigem artificialmente um `TenantContext` inicializado.

Essa validação protege o fluxo normal contra alterações explicitamente associadas a outro tenant, mas isoladamente não protege todos os cenários envolvendo entidades desconectadas.

### Proteção de escrita com entidades desconectadas

O `TenantId` das entidades `ITenantScoped` é configurado no Entity Framework Core como **concurrency token**.

Essa configuração cria uma segunda barreira para `UPDATE` e `DELETE`.

Quando uma entidade existente é alterada ou removida, o EF Core utiliza o valor original do `TenantId` como parte da condição utilizada para localizar o registro persistido.

Conceitualmente, uma alteração deixa de depender somente de:

```text
WHERE id = @id
```

e passa a exigir também a correspondência do tenant:

```text
WHERE id = @id
  AND tenant_id = @originalTenantId
```

Portanto, uma entidade desconectada não pode utilizar o `Id` de um registro pertencente ao Tenant B e simplesmente informar `TenantId = Tenant A` para modificá-lo ou excluí-lo.

Nesse cenário, nenhuma linha pertencente ao outro tenant corresponde ao predicado esperado. O Entity Framework Core detecta que nenhuma linha foi afetada e sinaliza a situação por meio de `DbUpdateConcurrencyException`.

A aplicação deve tratar essa exceção de acordo com o contexto da operação sem revelar a existência de recursos pertencentes a outro tenant.

### Por que não utilizar chave primária composta global

Não adotamos como regra global uma chave primária composta:

```text
(TenantId, Id)
```

para todas as entidades tenant-scoped.

Embora uma chave composta possa fornecer isolamento adicional em determinados relacionamentos, aplicá-la indiscriminadamente aumentaria:

- largura de índices;
- complexidade de foreign keys;
- propagação de chaves compostas entre relacionamentos;
- complexidade do modelo EF Core;
- custo de manutenção.

Para o estágio atual da arquitetura, a combinação de Query Filter, validação de ownership, concurrency token e constraints específicas oferece proteção proporcional sem introduzir complexidade estrutural desnecessária.

Constraints e foreign keys compostas envolvendo `TenantId` continuam permitidas e devem ser utilizadas quando trouxerem proteção real para relacionamentos críticos.

Essa decisão pode ser reavaliada conforme o domínio e os hot paths evoluírem.

### Defesa em profundidade

O isolamento multi-tenant não depende de uma única barreira.

A estratégia utiliza:

1. resolução confiável do tenant na entrada da operação;
2. `TenantContext` scoped;
3. Global Query Filters para isolamento de leitura;
4. validação de ownership antes da escrita;
5. `TenantId` como concurrency token para proteger `UPDATE` e `DELETE`;
6. constraints e índices PostgreSQL quando necessários;
7. testes arquiteturais;
8. testes de integração com PostgreSQL real.

Cada mecanismo cobre uma classe diferente de risco.

O Query Filter protege o fluxo normal de leitura.

A validação de ownership rejeita entidades cujo `TenantId` em memória não corresponde ao tenant corrente.

O concurrency token impede que uma entidade desconectada com ownership forjado altere ou exclua silenciosamente um registro pertencente a outro tenant.

### Testes de isolamento

Os testes de integração utilizam PostgreSQL real e validam cenários com tenants distintos.

A cobertura atual inclui:

- isolamento de listagem entre Tenant A e Tenant B;
- consulta direta por identificador pertencente a outro tenant;
- bloqueio de inserção cross-tenant;
- bloqueio de atualização cross-tenant;
- bloqueio de exclusão cross-tenant;
- tentativa de atualização usando `Id` de outro tenant e `TenantId` forjado;
- tentativa de exclusão usando `Id` de outro tenant e `TenantId` forjado;
- verificação física do registro com `IgnoreQueryFilters()` nos testes de segurança;
- persistência de entidades raiz sem contexto artificial de tenant.

Os testes que dependem de PostgreSQL compartilham uma fixture responsável por:

- obter a connection string de teste;
- inicializar o banco;
- aplicar migrations antes da execução dos testes.

A aplicação de migrations não deve ficar distribuída entre classes individuais de teste.

Além dos testes de integração, existe um teste arquitetural que verifica que entidades `ITenantScoped` mapeadas pelo EF Core possuem:

- Query Filter;
- propriedade `TenantId`;
- `TenantId` configurado como concurrency token.

O teste arquitetural funciona como guarda estrutural. Ele não tenta interpretar semanticamente a expressão do Query Filter, evitando acoplamento frágil aos detalhes internos do EF Core.

A garantia comportamental do isolamento continua sendo validada pelos testes de integração contra PostgreSQL real.

## Limites da implementação atual

A fundação de isolamento multi-tenant não implementa autenticação ou autorização.

A resolução confiável do `TenantId` a partir do usuário autenticado será implementada na etapa de Identity/Auth.

Essa etapa deverá estabelecer a associação entre identidade e tenant e inicializar o `TenantContext` sem aceitar o `TenantId` enviado pelo cliente como autoridade.

O comportamento HTTP para acesso cross-tenant, incluindo retorno `404` para recursos não visíveis ao tenant corrente, será implementado e validado na camada de API quando os endpoints autenticados forem introduzidos.

Jobs, webhooks e consumidores assíncronos também deverão estabelecer explicitamente um contexto de tenant confiável antes de executar operações tenant-scoped.

A correlação de logs por tenant será implementada quando os fluxos de entrada possuírem resolução confiável do tenant. Essa ausência nesta etapa não altera as garantias de isolamento fornecidas pela camada de persistência.

A infraestrutura atual de desenvolvimento e testes utiliza uma instância PostgreSQL configurada externamente por connection string.

A evolução da infraestrutura de CI deverá provisionar PostgreSQL de forma automatizada e isolada, podendo utilizar containers efêmeros ou mecanismo equivalente. Essa evolução pertence à etapa de CI e não é requisito para considerar a fundação atual de isolamento implementada.

## Consequências positivas

- segurança estrutural desde o início;
- proteção de leitura e escrita em camadas independentes;
- proteção contra `UPDATE` e `DELETE` cross-tenant com entidades desconectadas;
- modelo SaaS suportado nativamente;
- métricas e custos podem ser calculados por tenant;
- facilita Billing, Usage Metering e auditoria;
- evita adoção prematura de chaves compostas em todo o domínio.

## Consequências negativas

- exige disciplina em todas as consultas e comandos;
- aumenta a importância de testes de autorização e isolamento;
- entidades tenant-scoped exigem configuração consistente no EF Core;
- conflitos de ownership persistido podem se manifestar como `DbUpdateConcurrencyException`;
- índices e constraints precisam considerar o modelo Multi-Tenant.

## Não decidido agora

Não adotaremos no MVP:

- banco separado por tenant;
- schema PostgreSQL separado por tenant;
- infraestrutura dedicada por cliente;
- chave primária composta `(TenantId, Id)` como regra global.

Essas opções podem ser avaliadas futuramente para requisitos específicos de escala, contrato, relacionamento ou isolamento.