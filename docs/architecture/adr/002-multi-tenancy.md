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
